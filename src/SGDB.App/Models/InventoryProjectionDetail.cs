namespace SGDB.Models;

/// <summary>
/// Envelope somente leitura do detalhe de projeção (70D-B5).
/// Sem I/O, sem recálculo FEFO e sem consulta. A janela futura recebe este objeto.
/// </summary>
public sealed class InventoryProjectionDetail
{
    public required ProductTurnoverRow Intelligence { get; init; }
    public required InventoryProjectedProductPresentation Projection { get; init; }

    public IReadOnlyList<InventoryProjectedLotPresentation> Lots => Projection.Lots;
    public IReadOnlyList<string> Alerts => Projection.Alerts;
    public double TrackedLotQuantity => Projection.TrackedLotQuantity;
    public double UntrackedWarehouseQuantity => Projection.UntrackedWarehouseQuantity;
    public bool HasUntrackedWarehouse => Projection.HasUntrackedWarehouse;
    public string UntrackedWarehouseAlert => Projection.UntrackedWarehouseAlert;
    public bool HasLotLocationLimitation => Projection.HasLotLocationLimitation;
    public string FridgeLimitationAlert => Projection.FridgeLimitationAlert;

    /// <summary>
    /// Monta o detalhe a partir dos snapshots já carregados. productId inexistente → null.
    /// Não chama service, motor nem cadastro.
    /// </summary>
    public static InventoryProjectionDetail? TryCreate(
        InventoryProjectionSnapshot? snapshot,
        InventoryProjectionPresentationSnapshot? presented,
        int productId)
    {
        if (productId <= 0 || snapshot?.Intelligence.Rows is null)
            return null;

        ProductTurnoverRow? intelligence = null;
        foreach (var row in snapshot.Intelligence.Rows)
        {
            if (row.ProductId != productId)
                continue;
            intelligence = row;
            break;
        }

        if (intelligence is null)
            return null;

        InventoryProjectedProductPresentation? projection = null;
        presented?.ByProductId.TryGetValue(productId, out projection);
        if (projection is null)
        {
            snapshot.ByProductId.TryGetValue(productId, out var raw);
            projection = InventoryProjectionPresentation.FromProduct(
                raw ?? new InventoryProjectedProduct { ProductId = productId },
                intelligence);
        }

        return new InventoryProjectionDetail
        {
            Intelligence = intelligence,
            Projection = projection,
        };
    }
}
