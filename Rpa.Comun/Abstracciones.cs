using Rpa.Comun.Modelos;

namespace Rpa.Comun;

// Cola común de tareas en SQL Server INTERFAZ (dbo.RPA_TAREA, script SQL/01).
public interface ITareasCola
{
    // Toma la siguiente PEN de los flujos que sabe hacer este robot: un robot nunca toma tareas de otro sistema.
    Task<ClsTarea?> TomarAsync(string maquina, IReadOnlyCollection<string> flujos);
    Task MarcarAsync(int id, string estado, string mensaje, string? paso, string? resultado, string? captura, string maquina);
    Task<int> MarcarHuerfanasAsync(string maquina);
    Task LogAsync(int? idTarea, string nivel, string mensaje, string maquina);
    Task<ClsEncolado> EncolarAsync(ClsNuevaTarea tarea);
}

// Registro del robot en dbo.RPA_ROBOT (script SQL/02): en qué máquina está, qué flujos hace y si está vivo.
// Con esto el adaptador sabe a qué máquina va cada flujo, aunque los robots estén en servidores distintos.
public interface IRegistroRobot
{
    Task LatirAsync(string estado, string? detalle = null);
}

public static class EstadosRobot
{
    public const string Esperando = "ESPERANDO";
    public const string Trabajando = "TRABAJANDO";
    public const string Detenido = "DETENIDO";
}
