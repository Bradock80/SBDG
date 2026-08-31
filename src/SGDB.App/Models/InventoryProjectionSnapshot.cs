namespace SGDB.Models;

/// <summary>
/// Custo efetivo de um lote na composição 70D, com origem (lote vs estimativa atual).
/// </summary>
public sealed class InventoryProjectedLotCost
{
    public int LotId { get; init; }
    public double? UsedCost { get; init; }
    public LotCostSource CostSource { get; init; }
}

/// <summary>Projeção de um produto ativo da 70C + origem de custo por lote.</summary>
public sealed class InventoryProjectedProduct
{
    public int ProductId { get; init; }
    public InventoryProjectionResult Projection { get; init; } = new();
    public IReadOnlyList<InventoryProjectedLotCost> LotCosts { get; init; } = [];
}

/// <summary>
/// Snapshot composto 70D: 70C intacta + projeções por ProductId.
/// QueryCount = 6 (inteligência) + 1 (lotes).
/// </summary>
public sealed class InventoryProjectionSnapshot
{
    public DateTime Today { get; init; }
    public int QueryCount { get; init; }
    public InventoryIntelligenceSnapshot Intelligence { get; init; } = new();
    public IReadOnlyDictionary<int, InventoryProjectedProduct> ByProductId { get; init; } =
        new Dictionary<int, InventoryProjectedProduct>();
}
