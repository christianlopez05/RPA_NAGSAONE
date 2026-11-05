using System.Data;
using Microsoft.Data.SqlClient;

namespace Rpa.Comun.Datos;

// Latido del robot en dbo.RPA_ROBOT (script SQL/02).
public class RegistroRobotSql : IRegistroRobot
{
    private readonly string _cs;
    private readonly string _maquina;
    private readonly string _robot;
    private readonly string _flujos;
    private readonly string _version;

    public RegistroRobotSql(string connectionString, string maquina, string robot, IEnumerable<string> flujos, string version)
    {
        _cs = connectionString;
        _maquina = maquina;
        _robot = robot;
        _flujos = string.Join(",", flujos);
        _version = version;
    }

    public async Task LatirAsync(string estado, string? detalle = null)
    {
        await using var cn = new SqlConnection(_cs);
        await using var cmd = new SqlCommand("dbo.RPA_ROBOT_LATIDO", cn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 15 };
        cmd.Parameters.AddWithValue("@MAQUINA", _maquina);
        cmd.Parameters.AddWithValue("@ROBOT", _robot);
        cmd.Parameters.AddWithValue("@USUARIO_WIN", $@"{Environment.UserDomainName}\{Environment.UserName}");
        cmd.Parameters.AddWithValue("@FLUJOS", _flujos);
        cmd.Parameters.AddWithValue("@VERSION", _version);
        cmd.Parameters.AddWithValue("@ESTADO", estado);
        cmd.Parameters.AddWithValue("@DETALLE", (object?)detalle ?? DBNull.Value);
        await cn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }
}
