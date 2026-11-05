using FlaUI.Core.AutomationElements;
using Rpa.Comun.Modelos;
using Rpa.AW.Cliente;

namespace Rpa.AW.Pantallas;

// Pantalla de importación EDI de pedidos de A+W (menú Menu.ImportacionEdi), flujo rpa_aw_pedido_edi.
// PRIMERA VERSIÓN: el orden de pasos es una suposición hasta explorar la pantalla real con --menu y --probar-edi en la
// máquina del robot (contra demo2026). Selectores:
//   Menu.ImportacionEdi  ruta de la opción en el menú lateral, p. ej. "Importación EDI" o "Interfaces > Importación EDI"
//   Edi.Archivo          (opcional) campo donde se escribe la ruta del .ASC. Vacío = A+W lee su propia carpeta de
//                        importación: Edi:CarpetaArchivos debe apuntar a ella
//   Edi.Importar         botón o atajo que importa ("Atajo:..." o AutomationId/Name)
public class PantallaImportacionEdi : PantallaBase
{
    private readonly Capturador _cap;
    private readonly int _segundosRespuesta;

    public PantallaImportacionEdi(Localizador loc, Window ventana, Capturador cap, int segundosRespuesta) : base(loc, ventana)
    {
        _cap = cap;
        _segundosRespuesta = segundosRespuesta;
    }

    // Claves obligatorias; el robot avisa al arrancar si falta alguna y la tarea va a ERR sin tocar A+W.
    public static readonly string[] Claves = { "Menu.ImportacionEdi", "Edi.Importar" };

    // true en cuanto se presionó Importar: un error posterior pudo haber creado el pedido (va a revisión, no se reintenta).
    public bool ImportarPresionado { get; private set; }

    public static void ValidarFlujoConfigurado(Localizador loc)
    {
        var pendientes = loc.PendientesDe(Claves).ToList();
        if (pendientes.Count > 0)
            throw new FlujoNoConfiguradoException(
                $"La importación EDI aún no está configurada en el robot (selectores pendientes: {string.Join(", ", pendientes)}).");
    }

    public ClsResultadoEjecucion Importar(ClsTarea t, string rutaArchivo, bool modoEnsayo)
    {
        var pid = Ventana.Properties.ProcessId.Value;
        var hija = AbrirMenu("Menu.ImportacionEdi");
        Win32Ventanas.ActivarHijaMdi(hija);
        if (Loc.EstaConfigurado("Edi.Archivo"))
            Escribir("Edi.Archivo", rutaArchivo);
        _cap.Paso(t.Id, "edi_listo");

        if (modoEnsayo)
        {
            var capEnsayo = _cap.Tomar(t.Id, "edi_ensayo");
            Win32Ventanas.CerrarHijaMdi(hija, pid);
            return ClsResultadoEjecucion.Ok("Ensayo (no se importó)", capEnsayo);
        }

        ImportarPresionado = true;
        Clic("Edi.Importar");

        // Se juntan los mensajes de A+W durante la importación (resumen, avisos o errores). Nunca se responde Sí:
        // lo que A+W pregunte queda dicho en la tarea y la verificación en la base decide si se creó el pedido.
        var mensajes = new List<string>();
        var limite = DateTime.Now.AddSeconds(_segundosRespuesta);
        while (DateTime.Now < limite)
        {
            Thread.Sleep(1000);
            mensajes.AddRange(Win32Ventanas.AceptarMensajes(pid).Select(m => m.Texto));
        }
        return ClsResultadoEjecucion.Ok(string.Join(" | ", mensajes.Distinct()), _cap.Paso(t.Id, "edi_importado"));
    }
}

// El flujo no tiene sus selectores: reintentar no lo arregla (va a ERR sin tocar A+W).
public class FlujoNoConfiguradoException : Exception
{
    public FlujoNoConfiguradoException(string mensaje) : base(mensaje) { }
}
