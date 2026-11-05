using Rpa.Comun.Modelos;

namespace Rpa.Comun.Reglas;

// Decide el estado de una tarea cuando la pantalla falló (la regla de RPA_AW, ahora para todos los robots):
//   · Falló ANTES de guardar → no se registró nada: se reintenta con los mismos datos hasta MaxIntentos (PEN), luego ERR.
//   · Falló DESPUÉS de guardar → el sistema pudo haberlo registrado: REV (nunca se reintenta a ciegas).
//   · Error definitivo (el sistema rechazó el dato, configuración que no lo admite) → ERR sin reintentar.
public static class PoliticaReintentos
{
    public static string EstadoTrasFallo(ClsResultadoEjecucion resultado, int intentosPrevios, int maxIntentos)
    {
        if (resultado.GuardadoIntentado) return EstadosTarea.Revision;
        if (resultado.Definitivo) return EstadosTarea.Error;
        return intentosPrevios + 1 < maxIntentos ? EstadosTarea.Pendiente : EstadosTarea.Error;
    }
}
