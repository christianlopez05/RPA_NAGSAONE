// Copiado de RPA_AW (Wilthon Marcillo, commit 9af446f), sin cambios de lógica.
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using Microsoft.Extensions.Logging;
using Rpa.AW.Config;

namespace Rpa.AW.Cliente;

// Modo --probar-login: abre A+W, inicia sesión (sin SQL ni cola) y guarda el árbol de controles de la
// ventana principal + una captura, para ubicar después el menú y la pantalla de retales.
public static class PruebaLogin
{
    public static int Ejecutar(AwOptions opt, RobotOptions optRobot, IReadOnlyDictionary<string, string> selectores,
                               ILoggerFactory logs, bool cerrarAlTerminar)
    {
        var log = logs.CreateLogger("PruebaLogin");
        var carpeta = Path.Combine(Path.GetDirectoryName(optRobot.CarpetaCapturas) ?? @"C:\RPA_AW", "Exploracion");
        Directory.CreateDirectory(carpeta);
        var sello = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        using var sesion = new SesionAw(opt, logs.CreateLogger<SesionAw>());
        var loc = new Localizador(sesion.Automation, selectores, TimeSpan.FromSeconds(opt.SegundosEsperaControl));
        try
        {
            var ventana = sesion.VentanaPrincipal(loc);
            log.LogInformation("OK: ventana principal '{Titulo}' (clase {Clase})", ventana.Title, ventana.ClassName);
            Thread.Sleep(2000);

            var captura = Path.Combine(carpeta, $"login_{sello}.png");
            try { Capture.Screen().ToFile(captura); log.LogInformation("Captura: {Ruta}", captura); } catch { }

            var sb = new StringBuilder();
            sb.AppendLine($"VENTANA '{ventana.Title}'  Clase={ventana.ClassName}");
            Volcar(ventana, sb, 1, 6);
            sb.AppendLine().AppendLine("=== Ventanas hijas Win32 ===");
            sb.Append(Win32Ventanas.DescribirHijos(ventana.Properties.NativeWindowHandle.Value));
            var ruta = Path.Combine(carpeta, $"login_{sello}.txt");
            File.WriteAllText(ruta, sb.ToString(), Encoding.UTF8);
            var lineas = sb.ToString().Split('\n');
            log.LogInformation("Árbol de la ventana principal ({N} líneas) guardado en {Ruta}", lineas.Length, ruta);
            foreach (var l in lineas.Take(60)) Console.WriteLine(l.TrimEnd());
            if (lineas.Length > 60) Console.WriteLine("…");
            return 0;
        }
        catch (Exception ex)
        {
            log.LogError("{Tipo}: {Msg}", ex.GetType().Name, string.IsNullOrWhiteSpace(ex.Message) ? "(sin mensaje)" : ex.Message);
            try { Capture.Screen().ToFile(Path.Combine(carpeta, $"login_error_{sello}.png")); } catch { }
            return 1;
        }
        finally
        {
            if (cerrarAlTerminar) sesion.Cerrar();
        }
    }

    internal static void Volcar(AutomationElement el, StringBuilder sb, int nivel, int max)
    {
        if (nivel > max) return;
        AutomationElement[] hijos;
        try { hijos = el.FindAllChildren(); } catch { return; }
        foreach (var h in hijos)
        {
            string tipo, nombre, id, clase;
            try { tipo = h.ControlType.ToString(); } catch { tipo = "?"; }
            try { nombre = h.Name; } catch { nombre = ""; }
            try { id = h.AutomationId; } catch { id = ""; }
            try { clase = h.ClassName; } catch { clase = ""; }
            sb.AppendLine($"{new string(' ', nivel * 2)}[{tipo}] Name='{nombre}' AutomationId='{id}' Class='{clase}'");
            Volcar(h, sb, nivel + 1, max);
        }
    }
}
