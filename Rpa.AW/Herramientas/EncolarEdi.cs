using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rpa.Comun;
using Rpa.Comun.Modelos;
using Rpa.AW.Flujos;
using Rpa.AW.Flujos.PedidoEdi;
using Rpa.AW.Flujos.Modelos;
using Rpa.AW.Flujos.Reglas;

namespace Rpa.AW.Herramientas;

// --encolar-edi <archivo.ASC> --lote N [--cliente 398] [--empresa VETRIKO]
// Para probar sin la plataforma: deja en la cola común una tarea rpa_aw_pedido_edi con un .ASC descargado de la pantalla
// AwEdiVidrios. Origen PANTALLA y persona = el usuario de Windows que lo ejecuta (queda en la cola y en la bitácora).
// No abre A+W: el robot la toma en su siguiente ciclo, en ensayo mientras Edi:ModoEnsayo sea true.
public static class EncolarEdi
{
    public static async Task<int> Ejecutar(ITareasCola cola, EdiOptions opt, string[] args, ILoggerFactory logs)
    {
        var log = logs.CreateLogger("EncolarEdi");
        string? Arg(string nombre)
        {
            var i = Array.FindIndex(args, a => a.Equals(nombre, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var ruta = Arg("--encolar-edi");
        if (string.IsNullOrWhiteSpace(ruta) || !File.Exists(ruta))
        {
            log.LogError("Indique el archivo: --encolar-edi <archivo.ASC> --lote N [--cliente 398] [--empresa VETRIKO]. No existe '{Ruta}'.", ruta);
            return 1;
        }
        if (!int.TryParse(Arg("--lote"), out var lote))
        {
            log.LogError("Falta --lote N (el lote de PrefSuite del archivo).");
            return 1;
        }
        var cliente = int.TryParse(Arg("--cliente"), out var c) ? c : 398;

        var pedido = new ClsPedidoEdi
        {
            Lote = lote,
            ClienteAW = cliente,
            Archivo = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(ruta)),
        };
        var errores = ValidadorPedidoEdi.ValidarArchivo(pedido);
        if (errores.Count > 0)
        {
            log.LogError("El archivo no se encola: {Errores}", string.Join("; ", errores));
            return 1;
        }

        var datos = JsonSerializer.Serialize(new { lote = pedido.Lote, clienteAW = pedido.ClienteAW, archivo = pedido.Archivo });
        var r = await cola.EncolarAsync(new ClsNuevaTarea
        {
            Flujo = FlujosAw.PedidoEdi,
            VersionFlujo = opt.VersionFlujo,
            OperacionId = Guid.NewGuid(),
            Origen = OrigenesTarea.Pantalla,
            Persona = $@"{Environment.UserDomainName}\{Environment.UserName}",
            Empresa = Arg("--empresa") ?? "VETRIKO",
            Datos = datos,
        });
        log.LogInformation("Tarea #{Id} encolada ({Estado}) para el lote {Lote}, cliente A+W {Cliente}. {Modo}", r.Id, r.Estado, lote, cliente,
            opt.ModoEnsayo ? "Edi:ModoEnsayo = true: el robot la dejará en REV sin importar." : "Edi:ModoEnsayo = false: el robot IMPORTARÁ el pedido.");
        return 0;
    }
}
