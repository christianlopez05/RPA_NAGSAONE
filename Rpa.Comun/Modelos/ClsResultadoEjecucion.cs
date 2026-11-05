namespace Rpa.Comun.Modelos;

// Resultado de ejecutar un paso en la pantalla de un sistema (tomado de RPA_AW).
// GuardadoIntentado es la clave para decidir si se puede reintentar: si el robot ya presionó Guardar (o Importar)
// y luego falló, el sistema pudo haber registrado → no se reintenta (va a revisión).
public class ClsResultadoEjecucion
{
    public bool Exito { get; init; }
    public bool GuardadoIntentado { get; init; }
    public string Mensaje { get; init; } = string.Empty;
    public string? Captura { get; init; }
    // Error de dato que no se arregla reintentando (p. ej. el sistema rechazó el registro): va directo a ERR.
    public bool Definitivo { get; init; }
    // Paso donde falló, para la columna PASO_FALLIDO (vacío = lo decide el procesador).
    public string? Paso { get; init; }

    public static ClsResultadoEjecucion Ok(string mensaje = "", string? captura = null) =>
        new() { Exito = true, GuardadoIntentado = true, Mensaje = mensaje, Captura = captura };

    public static ClsResultadoEjecucion Fallo(string mensaje, bool guardadoIntentado, string? captura = null, bool definitivo = false,
                                              string? paso = null) =>
        new() { Exito = false, GuardadoIntentado = guardadoIntentado, Mensaje = mensaje, Captura = captura, Definitivo = definitivo, Paso = paso };
}
