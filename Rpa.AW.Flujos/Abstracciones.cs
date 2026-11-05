using Rpa.AW.Flujos.Modelos;
using Rpa.Comun.Modelos;

namespace Rpa.AW.Flujos;

// Flujos que sabe hacer el robot de A+W. El nombre es la herramienta del catálogo RpaFlows de B1AgentTeam.
public static class FlujosAw
{
    public const string PedidoEdi = "rpa_aw_pedido_edi";

    public static readonly IReadOnlyList<string> Todos = new[] { PedidoEdi };
}

// Lectura (solo lectura) de los pedidos de un lote en la base de A+W, para no duplicar y para verificar la importación.
public interface IPedidosAw
{
    Task<IReadOnlyList<ClsPedidoAw>> PedidosDeLoteAsync(int lote, int clienteAW);
}

// Importa un archivo EDI en la pantalla de A+W (implementación FlaUI en Rpa.AW).
public interface IImportadorEdi
{
    ClsResultadoEjecucion Importar(ClsTarea tarea, string rutaArchivo, bool modoEnsayo);
}
