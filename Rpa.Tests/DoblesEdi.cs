using System.Globalization;
using System.Text;
using System.Text.Json;
using Rpa.AW.Flujos;
using Rpa.Comun;
using Rpa.AW.Flujos.Modelos;
using Rpa.Comun.Modelos;

namespace Rpa.Tests;

// Dobles en memoria para probar ProcesadorPedidoEdi sin SQL Server ni A+W.

public class TareasFalsas : ITareasCola
{
    private readonly Queue<ClsTarea> _pendientes = new();
    public List<(int Id, string Estado, string Mensaje, string? Paso, string? Resultado)> Marcadas { get; } = new();

    public TareasFalsas(params ClsTarea[] tareas)
    {
        foreach (var t in tareas) _pendientes.Enqueue(t);
    }

    public Task<ClsTarea?> TomarAsync(string maquina, IReadOnlyCollection<string> flujos) =>
        Task.FromResult(_pendientes.Count > 0 && flujos.Contains(_pendientes.Peek().Flujo) ? _pendientes.Dequeue() : null);

    public Task MarcarAsync(int id, string estado, string mensaje, string? paso, string? resultado, string? captura, string maquina)
    {
        Marcadas.Add((id, estado, mensaje, paso, resultado));
        return Task.CompletedTask;
    }

    public Task<int> MarcarHuerfanasAsync(string maquina) => Task.FromResult(0);
    public Task LogAsync(int? idTarea, string nivel, string mensaje, string maquina) => Task.CompletedTask;
    public Task<ClsEncolado> EncolarAsync(ClsNuevaTarea tarea) => throw new NotSupportedException();

    public (int Id, string Estado, string Mensaje, string? Paso, string? Resultado) Ultima => Marcadas.Last();
}

// Pedidos de A+W simulados: "antes" en la primera consulta y "despues" en las siguientes.
public class PedidosFalsos : IPedidosAw
{
    private readonly ClsPedidoAw[] _antes;
    private readonly ClsPedidoAw[] _despues;
    private int _llamadas;

    public PedidosFalsos(ClsPedidoAw[]? antes = null, ClsPedidoAw[]? despues = null)
    {
        _antes = antes ?? Array.Empty<ClsPedidoAw>();
        _despues = despues ?? _antes;
    }

    public Task<IReadOnlyList<ClsPedidoAw>> PedidosDeLoteAsync(int lote, int clienteAW) =>
        Task.FromResult<IReadOnlyList<ClsPedidoAw>>(_llamadas++ == 0 ? _antes : _despues);
}

public class ImportadorFalso : IImportadorEdi
{
    private readonly ClsResultadoEjecucion _resultado;
    public int Llamadas { get; private set; }
    public bool? UltimoModoEnsayo { get; private set; }
    public string? UltimaRuta { get; private set; }

    public ImportadorFalso(ClsResultadoEjecucion resultado) => _resultado = resultado;

    public ClsResultadoEjecucion Importar(ClsTarea tarea, string rutaArchivo, bool modoEnsayo)
    {
        Llamadas++;
        UltimoModoEnsayo = modoEnsayo;
        UltimaRuta = rutaArchivo;
        return _resultado;
    }
}

// Arma un .ASC con la misma forma que ArchivoEdiAw (FH, K1, P1…, #END#; 512 caracteres + CRLF).
public static class ArchivoEdiDePrueba
{
    public static string Armar(int cliente = 398, string lote = "6539", int posiciones = 2)
    {
        static string Registro(string tipo, params (int Pos, string Texto)[] campos)
        {
            var b = new string(' ', 512).ToCharArray();
            tipo.CopyTo(0, b, 0, 2);
            foreach (var (pos, texto) in campos) texto.CopyTo(0, b, pos - 1, texto.Length);
            return new string(b);
        }

        var regs = new List<string>
        {
            Registro("K1", (3, "2"), (4, "1"), (5, cliente.ToString("00000000", CultureInfo.InvariantCulture)),
                (21, "20261105"), (29, lote.PadRight(40)), (69, "20545".PadRight(40)))
        };
        for (int i = 1; i <= posiciones; i++)
            regs.Add(Registro("P1", (3, "51000008".PadRight(64)), (247, "005410"), (253, "012350"), (259, "0000000400")));

        long total = (long)(regs.Count + 2) * 514;   // + FH + #END#
        regs.Insert(0, Registro("FH", (3, "02.2.035"), (11, total.ToString("000000000000", CultureInfo.InvariantCulture))));

        var sb = new StringBuilder();
        foreach (var r in regs) sb.Append(r).Append("\r\n");
        return sb.Append("#END#\r\n").ToString();
    }

    public static string Datos(int lote = 6539, int cliente = 398, string? archivo = null, int posiciones = 2) =>
        JsonSerializer.Serialize(new
        {
            lote, clienteAW = cliente, archivo = archivo ?? Armar(cliente, lote.ToString(), posiciones), posiciones, piezas = 8
        });
}
