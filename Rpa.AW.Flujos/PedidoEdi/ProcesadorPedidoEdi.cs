using System.Text;
using System.Text.Json;
using Rpa.AW.Flujos.Modelos;
using Rpa.Comun.Modelos;
using Rpa.AW.Flujos.Reglas;
using Rpa.Comun;
using Rpa.Comun.Reglas;

namespace Rpa.AW.Flujos.PedidoEdi;

public class EdiOptions
{
    // Apagado por defecto: el robot no toma tareas rpa_aw_pedido_edi hasta que se encienda.
    public bool Habilitado { get; set; }
    // Versión del flujo que sabe hacer este robot; una tarea de otra versión va a ERR sin tocar A+W.
    public int VersionFlujo { get; set; } = 1;
    // true = abre la importación y deja el archivo listo, pero NO importa: la tarea queda en REV.
    // Cambiar a false solo tras validar en demo2026.
    public bool ModoEnsayo { get; set; } = true;
    public int MaxIntentos { get; set; } = 3;
    // Cuánto se esperan los mensajes de A+W después de presionar Importar.
    public int SegundosRespuestaImportacion { get; set; } = 30;
    // A+W puede tardar en crear el pedido después de importar.
    public int SegundosVerificacion { get; set; } = 60;
    // Dónde se escribe el .ASC antes de importarlo (puede ser la carpeta de importación que lee A+W).
    public string CarpetaArchivos { get; set; } = @"C:\RPA_NAGSAONE\AW\Edi";
}

// Orquesta una tarea rpa_aw_pedido_edi:
//   tomar → validar DATOS y archivo → ¿el lote ya tiene pedido? → escribir el .ASC → importar en A+W → verificar → marcar.
// No conoce la pantalla (IImportadorEdi) ni la base (ITareasCola / IPedidosAw): se prueba con dobles.
// Reintentos como PoliticaReintentos: antes de importar, mismos datos hasta el tope; después de importar, REV.
public class ProcesadorPedidoEdi
{
    private static readonly string[] Flujos = { FlujosAw.PedidoEdi };

    private readonly ITareasCola _cola;
    private readonly IPedidosAw _pedidos;
    private readonly IImportadorEdi _importador;
    private readonly EdiOptions _opt;
    private readonly string _maquina;

    public ProcesadorPedidoEdi(ITareasCola cola, IPedidosAw pedidos, IImportadorEdi importador, EdiOptions opt, string maquina)
    {
        _cola = cola;
        _pedidos = pedidos;
        _importador = importador;
        _opt = opt;
        _maquina = maquina;
    }

    public bool ModoEnsayo => _opt.ModoEnsayo;

    public Task<int> RecuperarHuerfanasAsync() => _cola.MarcarHuerfanasAsync(_maquina);

    // Devuelve false si no había tareas.
    public async Task<bool> ProcesarSiguienteAsync()
    {
        var t = await _cola.TomarAsync(_maquina, Flujos);
        if (t == null) return false;

        if (t.VersionFlujo != _opt.VersionFlujo)
        {
            await Marcar(t, EstadosTarea.Error, $"La tarea es de la versión {t.VersionFlujo} del flujo y este robot hace la {_opt.VersionFlujo}.", "version");
            return true;
        }

        var (pedido, errores) = ValidadorPedidoEdi.Leer(t.Datos);
        if (pedido == null || errores.Count > 0)
        {
            await Marcar(t, EstadosTarea.Error, "Dato inválido: " + string.Join("; ", errores), "validar");
            return true;
        }

        // Sin duplicados: si el lote ya tiene pedido en A+W para ese cliente, no se importa otro.
        var antes = await _pedidos.PedidosDeLoteAsync(pedido.Lote, pedido.ClienteAW);
        var existentes = antes.Where(p => p.EsPedido).ToList();
        if (existentes.Count > 0)
        {
            await Marcar(t, EstadosTarea.Error,
                $"El lote {pedido.Lote} ya tiene pedido en A+W: {string.Join(", ", existentes.Select(p => $"{p.Pedido} (estado {p.Estado})"))}. No se importa otro.",
                "duplicado");
            return true;
        }

        string ruta;
        try
        {
            ruta = EscribirArchivo(t, pedido);
        }
        catch (Exception ex)
        {
            await Marcar(t, PoliticaReintentos.EstadoTrasFallo(ClsResultadoEjecucion.Fallo(ex.Message, false), t.Intentos, _opt.MaxIntentos),
                "No se pudo escribir el archivo: " + ex.Message, "archivo");
            return true;
        }

        ClsResultadoEjecucion r;
        try
        {
            r = _importador.Importar(t, ruta, _opt.ModoEnsayo);
        }
        catch (Exception ex)
        {
            // El importador debería capturar sus errores; si algo escapa, no se sabe si alcanzó a importar.
            r = ClsResultadoEjecucion.Fallo("Error no controlado en el robot: " + ex.Message, guardadoIntentado: true, paso: "importar");
        }

        if (!r.Exito)
        {
            var estado = PoliticaReintentos.EstadoTrasFallo(r, t.Intentos, _opt.MaxIntentos);
            var sufijo = estado == EstadosTarea.Pendiente ? $" (se reintentará, intento {t.Intentos + 1} de {_opt.MaxIntentos})" : "";
            await Marcar(t, estado, r.Mensaje + sufijo, r.Paso ?? (r.GuardadoIntentado ? "importar" : "preparar"), captura: r.Captura);
            return true;
        }

        if (_opt.ModoEnsayo)
        {
            await Marcar(t, EstadosTarea.Revision, "Modo ensayo: importación preparada en A+W, NO se importó.", "ensayo", captura: r.Captura);
            return true;
        }

        // Verificación: tiene que aparecer un pedido nuevo del lote para ese cliente.
        var idsAntes = antes.Select(p => p.Pedido).ToHashSet();
        var nuevos = await EsperarPedidosNuevos(pedido, idsAntes);
        var avisos = string.IsNullOrWhiteSpace(r.Mensaje) ? "" : $" A+W dijo: {r.Mensaje}";
        if (nuevos.Count == 1)
        {
            var n = nuevos[0];
            var resultado = JsonSerializer.Serialize(new { pedido = n.Pedido, estado = n.Estado, lote = pedido.Lote, clienteAW = pedido.ClienteAW });
            await Marcar(t, EstadosTarea.Ok, $"Pedido {n.Pedido} creado en A+W para el lote {pedido.Lote} (estado {n.Estado}).{avisos}",
                null, resultado, r.Captura);
        }
        else if (nuevos.Count > 1)
            await Marcar(t, EstadosTarea.Revision,
                $"Aparecieron {nuevos.Count} pedidos nuevos para el lote {pedido.Lote}: {string.Join(", ", nuevos.Select(p => p.Pedido))}. Revise en A+W si se duplicó.{avisos}",
                "verificar", captura: r.Captura);
        else
            await Marcar(t, EstadosTarea.Revision,
                $"Se importó en pantalla pero no apareció el pedido del lote {pedido.Lote} en A+W en {_opt.SegundosVerificacion} s. Revise en A+W antes de reprocesar.{avisos}",
                "verificar", captura: r.Captura);
        return true;
    }

    // El .ASC se escribe en Latin-1, como lo genera ArchivoEdiAw (el filtro de importación de A+W no es Unicode).
    private string EscribirArchivo(ClsTarea t, ClsPedidoEdi pedido)
    {
        Directory.CreateDirectory(_opt.CarpetaArchivos);
        var ruta = Path.Combine(_opt.CarpetaArchivos, $"LOTE{pedido.Lote}_{t.OperacionId:N}.ASC");
        File.WriteAllBytes(ruta, Encoding.Latin1.GetBytes(pedido.Archivo));
        return ruta;
    }

    private async Task<List<ClsPedidoAw>> EsperarPedidosNuevos(ClsPedidoEdi pedido, HashSet<int> idsAntes)
    {
        var limite = DateTime.Now.AddSeconds(_opt.SegundosVerificacion);
        while (true)
        {
            var nuevos = (await _pedidos.PedidosDeLoteAsync(pedido.Lote, pedido.ClienteAW))
                .Where(p => !idsAntes.Contains(p.Pedido)).ToList();
            if (nuevos.Count > 0 || DateTime.Now >= limite) return nuevos;
            await Task.Delay(2000);
        }
    }

    private Task Marcar(ClsTarea t, string estado, string mensaje, string? paso, string? resultado = null, string? captura = null) =>
        _cola.MarcarAsync(t.Id, estado, Recortar(mensaje, 1000), estado == EstadosTarea.Ok ? null : paso, resultado, captura, _maquina);

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];
}
