using System.Globalization;
using System.Text.Json;
using Rpa.AW.Flujos.Modelos;
using Rpa.Comun.Modelos;

namespace Rpa.AW.Flujos.Reglas;

// Lee los DATOS de una tarea rpa_aw_pedido_edi y revisa que el .ASC sea el que dice la tarea, antes de abrir A+W.
// Formato "A+W EDI Import" 02.2.035 tal como lo escribe ArchivoEdiAw (WEB_INTERFACES_CORE, módulo AW_EDI_VIDRIOS):
//   FH cabecera · K1 cabecera del pedido · P1 una posición por vidrio · #END#
//   cada registro 512 caracteres + CRLF; posiciones base 1, como en el manual.
// Un error aquí va a ERR sin tocar A+W: reintentar no lo arregla.
public static class ValidadorPedidoEdi
{
    public const int LargoRegistro = 512;
    private const string FinDeLinea = "\r\n";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static (ClsPedidoEdi? Pedido, List<string> Errores) Leer(string datos)
    {
        var errores = new List<string>();
        ClsPedidoEdi? p;
        try
        {
            p = JsonSerializer.Deserialize<ClsPedidoEdi>(datos ?? string.Empty, Json);
        }
        catch (JsonException ex)
        {
            return (null, new List<string> { "DATOS no es un JSON válido: " + ex.Message });
        }
        if (p == null) return (null, new List<string> { "DATOS está vacío." });

        if (p.Lote <= 0) errores.Add("Falta el lote.");
        if (p.ClienteAW <= 0) errores.Add("Falta el cliente A+W.");
        if (string.IsNullOrEmpty(p.Archivo)) errores.Add("Falta el archivo .ASC.");
        if (errores.Count > 0) return (p, errores);

        errores.AddRange(ValidarArchivo(p));
        return (p, errores);
    }

    public static List<string> ValidarArchivo(ClsPedidoEdi p)
    {
        var errores = new List<string>();
        var texto = p.Archivo;
        if (!texto.EndsWith("#END#" + FinDeLinea, StringComparison.Ordinal))
        {
            errores.Add("El archivo no termina en #END#.");
            return errores;
        }

        var registros = texto[..^(FinDeLinea.Length)].Split(FinDeLinea);
        var datos = registros[..^1];   // sin #END#
        for (int i = 0; i < datos.Length; i++)
            if (datos[i].Length != LargoRegistro)
                errores.Add($"El registro {i + 1} mide {datos[i].Length} caracteres; deben ser {LargoRegistro}.");
        if (errores.Count > 0) return errores;

        if (datos.Length < 3 || !datos[0].StartsWith("FH") || !datos[1].StartsWith("K1"))
        {
            errores.Add("El archivo debe empezar con FH y K1 y tener al menos una posición P1.");
            return errores;
        }
        var posiciones = datos.Skip(2).ToList();
        if (posiciones.Any(r => !r.StartsWith("P1")))
            errores.Add("Después de K1 solo se esperan registros P1.");

        var fh = datos[0];
        var total = Numero(fh, 11, 12);
        var esperado = (long)registros.Length * (LargoRegistro + FinDeLinea.Length);
        if (total != esperado)
            errores.Add($"FH/3 dice {total} caracteres y el archivo tiene {esperado}.");

        // La tarea y el archivo deben hablar del mismo pedido: cliente (K1/4) y lote (K1/7, texto de compra 1).
        var k1 = datos[1];
        if (Numero(k1, 3, 1) != 2 || Numero(k1, 4, 1) != 1)
            errores.Add("K1 no es un pedido nuevo (tipo 2, modo 1).");
        var cliente = Numero(k1, 5, 8);
        if (cliente != p.ClienteAW)
            errores.Add($"El archivo es del cliente A+W {cliente} y la tarea dice {p.ClienteAW}.");
        var lote = Alfa(k1, 29, 40);
        if (lote != p.Lote.ToString(CultureInfo.InvariantCulture))
            errores.Add($"El archivo es del lote '{lote}' y la tarea dice {p.Lote}.");

        if (p.Posiciones > 0 && p.Posiciones != posiciones.Count)
            errores.Add($"La tarea dice {p.Posiciones} posiciones y el archivo tiene {posiciones.Count}.");
        return errores;
    }

    private static string Alfa(string registro, int posicion, int largo) => registro.Substring(posicion - 1, largo).Trim();

    private static long Numero(string registro, int posicion, int largo) =>
        long.TryParse(registro.AsSpan(posicion - 1, largo), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1;
}
