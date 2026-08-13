namespace SGDB.Models;

/// <summary>
/// Origem do lote recebido em um item de compra.
/// Permanece mesmo quando product_lots mescla quantidades de várias compras.
/// </summary>
public sealed class PurchaseItemLot
{
    public int Id { get; init; }
    public int PurchaseItemId { get; init; }
    public int PurchaseId { get; init; }
    public int ProductId { get; init; }
    public string LotNumber { get; init; } = "";
    public DateTime? ExpiryDate { get; init; }
    public double Quantity { get; init; }
    public int? ProductLotId { get; init; }
    public string CreatedAt { get; init; } = "";
}
