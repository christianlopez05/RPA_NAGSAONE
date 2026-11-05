using System.Data;
using Microsoft.Data.SqlClient;
using Rpa.Comun.Modelos;

namespace Rpa.Comun.Datos;

// Cola común de tareas en SQL Server INTERFAZ (script SQL/01). Lectura por nombre de columna.
public class ColaTareasSql : ITareasCola
{
    private readonly string _cs;
    public ColaTareasSql(string connectionString) => _cs = connectionString;

    public async Task<ClsTarea?> TomarAsync(string maquina, IReadOnlyCollection<string> flujos)
    {
        if (flujos.Count == 0) return null;
        await using var cn = new SqlConnection(_cs);
        await using var cmd = Sp(cn, "dbo.RPA_TAREA_TOMAR");
        cmd.Parameters.AddWithValue("@MAQUINA", maquina);
        cmd.Parameters.AddWithValue("@FLUJOS", string.Join(",", flujos));
        await cn.OpenAsync();
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;

        return new ClsTarea
        {
            Id = rd.GetInt32(rd.GetOrdinal("ID")),
            Flujo = rd.GetString(rd.GetOrdinal("FLUJO")),
            VersionFlujo = rd.GetInt32(rd.GetOrdinal("VERSION_FLUJO")),
            OperacionId = rd.GetGuid(rd.GetOrdinal("OPERACION_ID")),
            Origen = rd.GetString(rd.GetOrdinal("ORIGEN")),
            Persona = rd.GetString(rd.GetOrdinal("PERSONA")),
            Empresa = rd.GetString(rd.GetOrdinal("EMPRESA")),
            Datos = rd.GetString(rd.GetOrdinal("DATOS")),
            Intentos = rd.GetInt32(rd.GetOrdinal("INTENTOS"))
        };
    }

    public async Task MarcarAsync(int id, string estado, string mensaje, string? paso, string? resultado, string? captura, string maquina)
    {
        await using var cn = new SqlConnection(_cs);
        await using var cmd = Sp(cn, "dbo.RPA_TAREA_MARCAR");
        cmd.Parameters.AddWithValue("@ID", id);
        cmd.Parameters.AddWithValue("@ESTADO", estado);
        cmd.Parameters.AddWithValue("@MENSAJE", mensaje);
        cmd.Parameters.AddWithValue("@PASO_FALLIDO", (object?)paso ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RESULTADO", (object?)resultado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CAPTURA", (object?)captura ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MAQUINA", maquina);
        await cn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> MarcarHuerfanasAsync(string maquina)
    {
        await using var cn = new SqlConnection(_cs);
        await using var cmd = Sp(cn, "dbo.RPA_TAREA_HUERFANAS");
        cmd.Parameters.AddWithValue("@MAQUINA", maquina);
        await cn.OpenAsync();
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task LogAsync(int? idTarea, string nivel, string mensaje, string maquina)
    {
        await using var cn = new SqlConnection(_cs);
        await using var cmd = Sp(cn, "dbo.RPA_TAREA_LOG_W");
        cmd.Parameters.AddWithValue("@ID_TAREA", (object?)idTarea ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@NIVEL", nivel);
        cmd.Parameters.AddWithValue("@MENSAJE", mensaje.Length > 2000 ? mensaje[..2000] : mensaje);
        cmd.Parameters.AddWithValue("@MAQUINA", maquina);
        await cn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<ClsEncolado> EncolarAsync(ClsNuevaTarea t)
    {
        await using var cn = new SqlConnection(_cs);
        await using var cmd = Sp(cn, "dbo.RPA_TAREA_ENCOLAR");
        cmd.Parameters.AddWithValue("@FLUJO", t.Flujo);
        cmd.Parameters.AddWithValue("@VERSION_FLUJO", t.VersionFlujo);
        cmd.Parameters.AddWithValue("@OPERACION_ID", t.OperacionId);
        cmd.Parameters.AddWithValue("@ORIGEN", t.Origen);
        cmd.Parameters.AddWithValue("@ENCARGO_ID", (object?)t.EncargoId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AGENTE_ID", (object?)t.AgenteId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PERSONA", t.Persona);
        cmd.Parameters.AddWithValue("@EMPRESA", t.Empresa);
        cmd.Parameters.Add("@DATOS", SqlDbType.NVarChar, -1).Value = t.Datos;
        await cn.OpenAsync();
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) throw new InvalidOperationException("RPA_TAREA_ENCOLAR no devolvió la tarea.");
        return new ClsEncolado(rd.GetInt32(0), rd.GetString(1), rd.GetBoolean(2));
    }

    private static SqlCommand Sp(SqlConnection cn, string nombre) =>
        new(nombre, cn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
}
