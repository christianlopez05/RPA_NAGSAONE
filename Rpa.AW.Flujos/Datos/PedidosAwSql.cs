using Microsoft.Data.SqlClient;
using Rpa.AW.Flujos.Modelos;
using Rpa.Comun.Modelos;

namespace Rpa.AW.Flujos.Datos;

// Pedidos de un lote en la base de A+W (ConnectionStrings:AwSql: demo2026 en pruebas, VETRIKO en producción). Solo lectura.
// Vetriko guarda el lote de PrefSuite en el texto de compra 1 (BEST_TEXT1), igual que lo lee la pantalla AwEdiVidrios.
public class PedidosAwSql : IPedidosAw
{
    private readonly string _cs;
    public PedidosAwSql(string connectionString) => _cs = connectionString;

    public async Task<IReadOnlyList<ClsPedidoAw>> PedidosDeLoteAsync(int lote, int clienteAW)
    {
        const string sql = @"
SELECT A.ID, A.STATUS, A.DATUM_ERF
FROM SYSADM.BW_AUFTR_KOPF AS A WITH (NOLOCK)
WHERE A.BEST_TEXT1 = @LOTE AND A.AH_IDENT = @CLIENTE
ORDER BY A.ID";
        var pedidos = new List<ClsPedidoAw>();
        await using var cn = new SqlConnection(_cs);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
        cmd.Parameters.Add("@LOTE", System.Data.SqlDbType.NVarChar, 40).Value = lote.ToString();
        cmd.Parameters.Add("@CLIENTE", System.Data.SqlDbType.Int).Value = clienteAW;
        await cn.OpenAsync();
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            pedidos.Add(new ClsPedidoAw(
                Convert.ToInt32(rd["ID"]),
                Convert.ToInt32(rd["STATUS"]),
                rd["DATUM_ERF"] is DateTime d ? d : null));
        return pedidos;
    }
}
