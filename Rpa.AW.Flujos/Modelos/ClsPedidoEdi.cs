namespace Rpa.AW.Flujos.Modelos;

// DATOS de una tarea rpa_aw_pedido_edi: el pedido de vidrio de un lote de PrefSuite, con el archivo .ASC ya armado.
// Lo arma quien encola (la pantalla AwEdiVidrios o el adaptador del agente "Pedidos de vidrio A+W") con ArchivoEdiAw;
// es exactamente el archivo que la persona confirmó.
public class ClsPedidoEdi
{
    public int Lote { get; set; }
    public int ClienteAW { get; set; }
    // Contenido del .ASC (registros de 512 caracteres + CRLF, termina en #END#). Se escribe en Latin-1.
    public string Archivo { get; set; } = string.Empty;
    public int Posiciones { get; set; }
    public int Piezas { get; set; }
}

// Pedido que ya está en A+W para un lote (SYSADM.BW_AUFTR_KOPF, BEST_TEXT1 = lote).
public record ClsPedidoAw(int Pedido, int Estado, DateTime? Fecha)
{
    // Estados desde 401 son pedidos; los menores, ofertas (mismo criterio que la pantalla AwEdiVidrios).
    public bool EsPedido => Estado >= 401;
}
