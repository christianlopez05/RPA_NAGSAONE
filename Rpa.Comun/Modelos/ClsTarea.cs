namespace Rpa.Comun.Modelos;

// Una tarea de la cola común dbo.RPA_TAREA (SQL/01), tomada con RPA_TAREA_TOMAR.
// Cada tarea tiene dueño (ORIGEN + PERSONA): un robot nunca trabaja por su cuenta (docs/rpa-robots.md de B1AgentTeam).
public class ClsTarea
{
    public int Id { get; set; }
    public string Flujo { get; set; } = string.Empty;
    public int VersionFlujo { get; set; }
    public Guid OperacionId { get; set; }
    public string Origen { get; set; } = string.Empty;
    public string Persona { get; set; } = string.Empty;
    public string Empresa { get; set; } = string.Empty;
    // JSON de entrada, ya validado por quien encoló. El robot no recalcula nada: ejecuta lo que se confirmó.
    public string Datos { get; set; } = string.Empty;
    public int Intentos { get; set; }

    public override string ToString() => $"#{Id} {Flujo} v{VersionFlujo} ({Origen}, {Persona}, {Empresa})";
}

// Para encolar (adaptador MCP de B1AgentTeam, una pantalla o un comando de prueba).
public class ClsNuevaTarea
{
    public string Flujo { get; set; } = string.Empty;
    public int VersionFlujo { get; set; }
    public Guid OperacionId { get; set; }
    public string Origen { get; set; } = OrigenesTarea.Pantalla;
    public Guid? EncargoId { get; set; }
    public string? AgenteId { get; set; }
    public string Persona { get; set; } = string.Empty;
    public string Empresa { get; set; } = string.Empty;
    public string Datos { get; set; } = string.Empty;
}

public record ClsEncolado(int Id, string Estado, bool Nueva);

public static class OrigenesTarea
{
    public const string Agente = "AGENTE";
    public const string Pantalla = "PANTALLA";
}

// Estados de la cola (CHECK CK_RPA_TAREA_ESTADO). Los mismos para todos los robots.
public static class EstadosTarea
{
    public const string Pendiente = "PEN";
    public const string EnProceso = "PRO";
    public const string Ok = "OK";
    public const string Error = "ERR";
    public const string Revision = "REV";
}
