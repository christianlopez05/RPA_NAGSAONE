using Rpa.AW.Flujos.Modelos;
using Rpa.Comun.Modelos;
using Rpa.AW.Flujos;
using Rpa.AW.Flujos.PedidoEdi;
using Rpa.AW.Flujos.Reglas;

namespace Rpa.Tests;

public class ProcesadorPedidoEdiTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "rpa_aw_edi_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, true); } catch { }
    }

    private static ClsTarea Tarea(string? datos = null, int intentos = 0, int version = 1) => new()
    {
        Id = 7, Flujo = FlujosAw.PedidoEdi, VersionFlujo = version, OperacionId = Guid.NewGuid(),
        Origen = OrigenesTarea.Agente, Persona = "rgarcia", Empresa = "VETRIKO",
        Datos = datos ?? ArchivoEdiDePrueba.Datos(), Intentos = intentos
    };

    private (ProcesadorPedidoEdi proc, TareasFalsas cola, ImportadorFalso imp) Armar(
        ClsTarea tarea, ClsResultadoEjecucion resultado, ClsPedidoAw[]? antes = null, ClsPedidoAw[]? despues = null, bool ensayo = false)
    {
        var cola = new TareasFalsas(tarea);
        var imp = new ImportadorFalso(resultado);
        var opt = new EdiOptions { Habilitado = true, ModoEnsayo = ensayo, MaxIntentos = 3, SegundosVerificacion = 0, CarpetaArchivos = _carpeta };
        return (new ProcesadorPedidoEdi(cola, new PedidosFalsos(antes, despues), imp, opt, "TEST"), cola, imp);
    }

    private static readonly ClsPedidoAw PedidoNuevo = new(12004624, 401, new DateTime(2026, 11, 5));

    [Fact]
    public async Task Sin_tareas_devuelve_false()
    {
        var (proc, _, imp) = Armar(Tarea(), ClsResultadoEjecucion.Ok());
        Assert.True(await proc.ProcesarSiguienteAsync());
        Assert.False(await proc.ProcesarSiguienteAsync());
        Assert.Equal(1, imp.Llamadas);
    }

    [Fact]
    public async Task Importa_verifica_y_deja_el_pedido_en_el_resultado()
    {
        var (proc, cola, imp) = Armar(Tarea(), ClsResultadoEjecucion.Ok(), despues: new[] { PedidoNuevo });
        await proc.ProcesarSiguienteAsync();

        Assert.Equal(EstadosTarea.Ok, cola.Ultima.Estado);
        Assert.Null(cola.Ultima.Paso);
        Assert.Contains("\"pedido\":12004624", cola.Ultima.Resultado);
        Assert.False(imp.UltimoModoEnsayo);
        Assert.True(File.Exists(imp.UltimaRuta));
    }

    [Fact]
    public async Task El_archivo_se_escribe_en_Latin1_tal_cual()
    {
        var archivo = ArchivoEdiDePrueba.Armar();
        var (proc, _, imp) = Armar(Tarea(ArchivoEdiDePrueba.Datos(archivo: archivo)), ClsResultadoEjecucion.Ok(), despues: new[] { PedidoNuevo });
        await proc.ProcesarSiguienteAsync();

        Assert.Equal(System.Text.Encoding.Latin1.GetBytes(archivo), File.ReadAllBytes(imp.UltimaRuta!));
    }

    [Fact]
    public async Task Lote_con_pedido_va_a_ERR_sin_abrir_AW()
    {
        var (proc, cola, imp) = Armar(Tarea(), ClsResultadoEjecucion.Ok(), antes: new[] { PedidoNuevo });
        await proc.ProcesarSiguienteAsync();

        Assert.Equal(EstadosTarea.Error, cola.Ultima.Estado);
        Assert.Equal("duplicado", cola.Ultima.Paso);
        Assert.Contains("12004624", cola.Ultima.Mensaje);
        Assert.Equal(0, imp.Llamadas);
    }

    [Fact]
    public async Task Una_oferta_del_lote_no_bloquea()
    {
        var oferta = new ClsPedidoAw(11000001, 200, null);
        var (proc, cola, _) = Armar(Tarea(), ClsResultadoEjecucion.Ok(), antes: new[] { oferta }, despues: new[] { oferta, PedidoNuevo });
        await proc.ProcesarSiguienteAsync();

        Assert.Equal(EstadosTarea.Ok, cola.Ultima.Estado);
        Assert.Contains("12004624", cola.Ultima.Resultado);
    }

    [Fact]
    public async Task Archivo_de_otro_lote_va_a_ERR_sin_abrir_AW()
    {
        var datos = ArchivoEdiDePrueba.Datos(lote: 6539, archivo: ArchivoEdiDePrueba.Armar(lote: "6540"));
        var (proc, cola, imp) = Armar(Tarea(datos), ClsResultadoEjecucion.Ok());
        await proc.ProcesarSiguienteAsync();

        Assert.Equal(EstadosTarea.Error, cola.Ultima.Estado);
        Assert.Equal("validar", cola.Ultima.Paso);
        Assert.Contains("'6540'", cola.Ultima.Mensaje);
        Assert.Equal(0, imp.Llamadas);
    }

    [Fact]
    public async Task Otra_version_del_flujo_va_a_ERR()
    {
        var (proc, cola, imp) = Armar(Tarea(version: 2), ClsResultadoEjecucion.Ok());
        await proc.ProcesarSiguienteAsync();

        Assert.Equal(EstadosTarea.Error, cola.Ultima.Estado);
        Assert.Equal("version", cola.Ultima.Paso);
        Assert.Equal(0, imp.Llamadas);
    }

    [Fact]
    public async Task Ensayo_queda_en_REV_y_no_verifica()
    {
        var (proc, cola, imp) = Armar(Tarea(), ClsResultadoEjecucion.Ok("Ensayo"), ensayo: true);
        await proc.ProcesarSiguienteAsync();

        Assert.Equal(EstadosTarea.Revision, cola.Ultima.Estado);
        Assert.Equal("ensayo", cola.Ultima.Paso);
        Assert.True(imp.UltimoModoEnsayo);
    }

    [Fact]
    public async Task Falla_antes_de_importar_se_reintenta_con_los_mismos_datos()
    {
        var (proc, cola, _) = Armar(Tarea(intentos: 0),
            ClsResultadoEjecucion.Fallo("No apareció la pantalla", guardadoIntentado: false, paso: "abrir"));
        await proc.ProcesarSiguienteAsync();

        Assert.Equal(EstadosTarea.Pendiente, cola.Ultima.Estado);
        Assert.Equal("abrir", cola.Ultima.Paso);
        Assert.Contains("intento 1 de 3", cola.Ultima.Mensaje);
    }

    [Fact]
    public async Task Falla_antes_de_importar_agota_intentos_y_va_a_ERR()
    {
        var (proc, cola, _) = Armar(Tarea(intentos: 2), ClsResultadoEjecucion.Fallo("No apareció la pantalla", guardadoIntentado: false));
        await proc.ProcesarSiguienteAsync();

        Assert.Equal(EstadosTarea.Error, cola.Ultima.Estado);
        Assert.Equal("preparar", cola.Ultima.Paso);
    }

    [Fact]
    public async Task Falla_despues_de_importar_va_a_REV_sin_reintentar()
    {
        var (proc, cola, _) = Armar(Tarea(), ClsResultadoEjecucion.Fallo("A+W se cerró", guardadoIntentado: true));
        await proc.ProcesarSiguienteAsync();

        Assert.Equal(EstadosTarea.Revision, cola.Ultima.Estado);
        Assert.Equal("importar", cola.Ultima.Paso);
    }

    [Fact]
    public async Task Importo_pero_no_aparece_el_pedido_va_a_REV()
    {
        var (proc, cola, _) = Armar(Tarea(), ClsResultadoEjecucion.Ok("Importación terminada"));
        await proc.ProcesarSiguienteAsync();

        Assert.Equal(EstadosTarea.Revision, cola.Ultima.Estado);
        Assert.Equal("verificar", cola.Ultima.Paso);
        Assert.Contains("A+W dijo: Importación terminada", cola.Ultima.Mensaje);
    }

    [Fact]
    public async Task Dos_pedidos_nuevos_van_a_REV_por_posible_duplicado()
    {
        var otro = new ClsPedidoAw(12004625, 401, null);
        var (proc, cola, _) = Armar(Tarea(), ClsResultadoEjecucion.Ok(), despues: new[] { PedidoNuevo, otro });
        await proc.ProcesarSiguienteAsync();

        Assert.Equal(EstadosTarea.Revision, cola.Ultima.Estado);
        Assert.Contains("12004625", cola.Ultima.Mensaje);
    }
}

public class ValidadorPedidoEdiTests
{
    [Fact]
    public void Archivo_bien_formado_no_tiene_errores()
    {
        var (p, errores) = ValidadorPedidoEdi.Leer(ArchivoEdiDePrueba.Datos());
        Assert.Empty(errores);
        Assert.Equal(6539, p!.Lote);
    }

    [Fact]
    public void JSON_invalido_se_informa()
    {
        var (_, errores) = ValidadorPedidoEdi.Leer("{no es json");
        Assert.Single(errores);
        Assert.StartsWith("DATOS no es un JSON válido", errores[0]);
    }

    [Fact]
    public void Cliente_distinto_al_de_la_tarea_es_error()
    {
        var (_, errores) = ValidadorPedidoEdi.Leer(ArchivoEdiDePrueba.Datos(cliente: 398, archivo: ArchivoEdiDePrueba.Armar(cliente: 2)));
        Assert.Contains(errores, e => e.Contains("cliente A+W 2"));
    }

    [Fact]
    public void Registro_mal_medido_es_error()
    {
        var archivo = ArchivoEdiDePrueba.Armar().Replace("51000008", "510000081");
        var (_, errores) = ValidadorPedidoEdi.Leer(ArchivoEdiDePrueba.Datos(archivo: archivo));
        Assert.Contains(errores, e => e.Contains("mide 513"));
    }

    [Fact]
    public void Sin_END_es_error()
    {
        var archivo = ArchivoEdiDePrueba.Armar().Replace("#END#\r\n", "");
        var (_, errores) = ValidadorPedidoEdi.Leer(ArchivoEdiDePrueba.Datos(archivo: archivo));
        Assert.Contains(errores, e => e.Contains("#END#"));
    }

    [Fact]
    public void Posiciones_distintas_a_las_de_la_tarea_es_error()
    {
        var datos = ArchivoEdiDePrueba.Datos(archivo: ArchivoEdiDePrueba.Armar(posiciones: 3), posiciones: 2);
        var (_, errores) = ValidadorPedidoEdi.Leer(datos);
        Assert.Contains(errores, e => e.Contains("2 posiciones") && e.Contains("tiene 3"));
    }
}
