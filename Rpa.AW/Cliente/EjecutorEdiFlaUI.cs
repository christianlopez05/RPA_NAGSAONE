using Microsoft.Extensions.Logging;
using Rpa.Comun;
using Rpa.Comun.Modelos;
using Rpa.AW.Flujos;
using Rpa.AW.Flujos.PedidoEdi;
using Rpa.AW.Pantallas;

namespace Rpa.AW.Cliente;

// Implementación real de IImportadorEdi: importa el .ASC en el cliente A+W con FlaUI.
// Nunca lanza excepciones hacia el procesador: todo error vuelve como ClsResultadoEjecucion.Fallo con captura.
public class EjecutorEdiFlaUI : IImportadorEdi
{
    private readonly SesionAw _sesion;
    private readonly Localizador _loc;
    private readonly Capturador _cap;
    private readonly EdiOptions _opt;
    private readonly ILogger<EjecutorEdiFlaUI> _log;

    public EjecutorEdiFlaUI(SesionAw sesion, Localizador loc, Capturador cap, EdiOptions opt, ILogger<EjecutorEdiFlaUI> log)
    {
        _sesion = sesion;
        _loc = loc;
        _cap = cap;
        _opt = opt;
        _log = log;
    }

    public ClsResultadoEjecucion Importar(ClsTarea tarea, string rutaArchivo, bool modoEnsayo)
    {
        _log.LogInformation("Importando {Tarea} desde {Ruta}{Ensayo}", tarea, rutaArchivo, modoEnsayo ? " [ENSAYO]" : "");
        PantallaImportacionEdi? pantalla = null;
        try
        {
            PantallaImportacionEdi.ValidarFlujoConfigurado(_loc);   // antes de abrir A+W
            var ventana = _sesion.VentanaPrincipal(_loc);
            pantalla = new PantallaImportacionEdi(_loc, ventana, _cap, _opt.SegundosRespuestaImportacion);
            var r = pantalla.Importar(tarea, rutaArchivo, modoEnsayo);
            _log.LogInformation("#{Id}: importación {Resultado}", tarea.Id, string.IsNullOrWhiteSpace(r.Mensaje) ? "sin mensajes de A+W" : r.Mensaje);
            return r;
        }
        catch (SelectorNoConfiguradoException ex)
        {
            return ClsResultadoEjecucion.Fallo(ex.Message, guardadoIntentado: false, paso: "configuracion");
        }
        catch (FlujoNoConfiguradoException ex)
        {
            return ClsResultadoEjecucion.Fallo(ex.Message, guardadoIntentado: false, definitivo: true, paso: "configuracion");
        }
        catch (Exception ex)
        {
            // Antes de presionar Importar (incluido abrir A+W o iniciar sesión) no se creó nada: se puede reintentar.
            // Después se asume que pudo crear el pedido → revisión.
            var importado = pantalla?.ImportarPresionado ?? false;
            var cap = _cap.Tomar(tarea.Id, "edi_excepcion");
            _log.LogError("#{Id}: error en la importación EDI ({Paso}): {Msg}", tarea.Id,
                importado ? "después de Importar" : "antes de Importar", ex.Message);
            return ClsResultadoEjecucion.Fallo($"Error en pantalla A+W: {ex.Message}", guardadoIntentado: importado, cap,
                paso: importado ? "importar" : "abrir");
        }
    }
}
