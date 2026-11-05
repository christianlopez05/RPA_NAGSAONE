using System.Text;
using FlaUI.Core.Capturing;
using Microsoft.Extensions.Logging;
using Rpa.AW.Cliente;
using Rpa.AW.Config;

namespace Rpa.AW.Herramientas;

// Exploración de la importación EDI (flujo rpa_aw_pedido_edi). Ninguno importa ni graba nada.
//   --menu        inicia sesión y guarda todas las opciones del menú lateral: sirve para encontrar la de importar EDI
//                 y completar Menu.ImportacionEdi.
//   --probar-edi  inicia sesión, abre Menu.ImportacionEdi y guarda captura y controles (UIA y Win32) para completar
//                 Edi.Archivo y Edi.Importar.
// Todo queda en la carpeta Exploracion, junto a la de capturas.
public static class PruebaEdi
{
    public static int Menu(AwOptions opt, RobotOptions optRobot, IReadOnlyDictionary<string, string> selectores,
                           ILoggerFactory logs, bool cerrarAlTerminar) =>
        Explorar("menu", opt, optRobot, selectores, logs, cerrarAlTerminar, (sesion, loc, principal, sb, log) =>
        {
            sb.Append(MenuAw.DescribirMenu(principal));
            log.LogInformation("Opciones del menú leídas. Busque la de importar pedidos EDI y póngala en Selectores:Menu.ImportacionEdi.");
        });

    public static int ProbarImportacion(AwOptions opt, RobotOptions optRobot, IReadOnlyDictionary<string, string> selectores,
                                        ILoggerFactory logs, bool cerrarAlTerminar) =>
        Explorar("edi", opt, optRobot, selectores, logs, cerrarAlTerminar, (sesion, loc, principal, sb, log) =>
        {
            var ruta = loc.Valor("Menu.ImportacionEdi");
            var titulo = ruta.Split('>', StringSplitOptions.TrimEntries)[^1];
            var pantalla = MenuAw.AbrirPantalla(principal, ruta, titulo, TimeSpan.FromSeconds(30), m => log.LogInformation("{M}", m));
            log.LogInformation("OK: pantalla '{Titulo}' abierta. No se importa nada.", titulo);
            Thread.Sleep(1500);
            foreach (var aviso in Win32Ventanas.AceptarMensajes(sesion.ProcessId))
                log.LogWarning("A+W mostró un mensaje al abrir: {Aviso}", aviso.Texto);

            sb.AppendLine($"PANTALLA '{titulo}'  hwnd=0x{pantalla.ToInt64():X}");
            sb.AppendLine("=== UI Automation ===");
            try { PruebaLogin.Volcar(sesion.Automation.FromHandle(pantalla), sb, 1, 8); }
            catch (Exception ex) { sb.AppendLine($"(UIA no disponible: {ex.Message})"); }
            sb.AppendLine().AppendLine("=== Ventanas hijas Win32 ===");
            sb.Append(Win32Ventanas.DescribirHijos(pantalla));
        });

    private static int Explorar(string nombre, AwOptions opt, RobotOptions optRobot, IReadOnlyDictionary<string, string> selectores,
        ILoggerFactory logs, bool cerrarAlTerminar, Action<SesionAw, Localizador, IntPtr, StringBuilder, ILogger> accion)
    {
        var log = logs.CreateLogger("PruebaEdi");
        var carpeta = Path.Combine(Path.GetDirectoryName(optRobot.CarpetaCapturas) ?? @"C:\RPA_NAGSAONE\AW", "Exploracion");
        Directory.CreateDirectory(carpeta);
        var sello = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        using var sesion = new SesionAw(opt, logs.CreateLogger<SesionAw>());
        var loc = new Localizador(sesion.Automation, selectores, TimeSpan.FromSeconds(opt.SegundosEsperaControl));
        try
        {
            var ventana = sesion.VentanaPrincipal(loc);
            var sb = new StringBuilder();
            accion(sesion, loc, ventana.Properties.NativeWindowHandle.Value, sb, log);

            var captura = Path.Combine(carpeta, $"{nombre}_{sello}.png");
            try { Capture.Screen().ToFile(captura); log.LogInformation("Captura: {Ruta}", captura); } catch { }
            var archivo = Path.Combine(carpeta, $"{nombre}_{sello}.txt");
            File.WriteAllText(archivo, sb.ToString(), Encoding.UTF8);
            log.LogInformation("Guardado en {Ruta}", archivo);
            return 0;
        }
        catch (Exception ex)
        {
            log.LogError("{Tipo}: {Msg}", ex.GetType().Name, string.IsNullOrWhiteSpace(ex.Message) ? "(sin mensaje)" : ex.Message);
            try { Capture.Screen().ToFile(Path.Combine(carpeta, $"{nombre}_error_{sello}.png")); } catch { }
            return 1;
        }
        finally
        {
            if (cerrarAlTerminar) sesion.Cerrar();
        }
    }
}
