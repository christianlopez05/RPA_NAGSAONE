// Copiado de RPA_AW (Wilthon Marcillo, commit 9af446f), sin cambios de lógica.
using FlaUI.Core.Capturing;
using Rpa.AW.Config;

namespace Rpa.AW.Cliente;

// Capturas de pantalla para diagnosticar errores (la ruta queda en RPA_TAREA.CAPTURA).
public class Capturador
{
    private readonly RobotOptions _opt;
    public Capturador(RobotOptions opt) => _opt = opt;

    public string? Tomar(int idCola, string etiqueta)
    {
        try
        {
            var carpeta = Path.Combine(_opt.CarpetaCapturas, DateTime.Now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(carpeta);
            var ruta = Path.Combine(carpeta, $"{idCola}_{DateTime.Now:HHmmss}_{etiqueta}.png");
            Capture.Screen().ToFile(ruta);
            return ruta;
        }
        catch
        {
            return null;   // una captura fallida nunca debe tumbar el procesamiento
        }
    }

    public string? Paso(int idCola, string etiqueta) => _opt.CapturaEnCadaPaso ? Tomar(idCola, etiqueta) : null;
}
