namespace SGDB.Models;

/// <summary>
/// Evidência histórica V1 de coocorrência por transação. Thresholds 5/3 são operacionais,
/// não significância estatística.
/// </summary>
public enum InventoryComboPairEvidence
{
    InsufficientHistory = 0,
    NoneObserved,
    Weak,
    Observed,
    InvalidCounts,
}

/// <summary>
/// Fatos de um par target/âncora. Sem ranking, preço, piso ou sugestão.
/// </summary>
public sealed class InventoryComboPairCoOccurrenceFacts
{
    public int TargetProductId { get; init; }
    public int AnchorProductId { get; init; }
    public int PairTransactions { get; init; }
    public int TargetTransactions { get; init; }
    public double? ConfidenceTargetToAnchor { get; init; }
    public InventoryComboPairEvidence Evidence { get; init; }
}

/// <summary>
/// Lote B2. QueryCount = 1 com pares candidatos; 0 se a lista efetiva for vazia.
/// </summary>
public sealed class InventoryComboCoOccurrenceSnapshot
{
    public int QueryCount { get; init; }
    public IReadOnlyList<int> RequestedTargetIds { get; init; } = [];
    public IReadOnlyList<int> RequestedAnchorIds { get; init; } = [];
    public IReadOnlyList<InventoryComboPairCoOccurrenceFacts> Rows { get; init; } = [];
}
