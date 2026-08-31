namespace SGDB.Models;

/// <summary>
/// Bloqueio da projeção SKU (demanda/excesso). Independente da validade/lote.
/// </summary>
public enum InventorySkuProjectionBlockedReason
{
    None = 0,
    InvalidInput,
    CompositionProduct,
    NoPhysicalEvidence,
    InsufficientHistory,
    NegativeStock,
    NegativeLocationStock,
    InconsistentStockTotals,
    NoObservableDemand,
}

/// <summary>
/// Bloqueio da projeção de validade/lote. Independente do excesso SKU.
/// </summary>
public enum InventoryExpiryProjectionBlockedReason
{
    None = 0,
    InvalidInput,
    CompositionProduct,
    NoPhysicalEvidence,
    InsufficientHistory,
    NoObservableDemand,
    NegativeWarehouseStock,
    NegativeLocationStock,
    InconsistentStockTotals,
    DuplicateLotId,
    InvalidLotQuantity,
    TrackedQuantityExceedsWarehouse,
}

/// <summary>
/// Classificação do lote. ExpiresToday continua válido (70I); não emite sobra numérica
/// porque a projeção é em dias civis, sem modelo intradiário.
/// </summary>
public enum InventoryProjectionLotKind
{
    Dated = 0,
    Undated,
    AlreadyExpired,
    ExpiresToday,
}

/// <summary>
/// Lote em memória para 70D-B1. Sem I/O. Sem localização.
/// UnitCost opcional: inválido só anula o valor, não a quantidade.
/// </summary>
public sealed class InventoryProjectionLotInput
{
    public int LotId { get; init; }
    public double Quantity { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public double? UnitCost { get; init; }
}

/// <summary>Pedido puro de projeção. Todos os campos vêm do chamador.</summary>
public sealed class InventoryProjectionRequest
{
    public DateTime Today { get; init; }
    public double Vmv30 { get; init; }
    public int HistoryDays { get; init; }
    public bool IsHistoryInsufficient30 { get; init; }
    public bool HasPhysicalAvailabilityEvidence { get; init; }
    public bool IsCompositionProduct { get; init; }
    public double TotalStock { get; init; }
    public double WarehouseStock { get; init; }
    public double FridgeStock { get; init; }
    public int HorizonDays { get; init; }
    public IReadOnlyList<InventoryProjectionLotInput> Lots { get; init; } = [];
}

/// <summary>
/// Resultado de um lote. ProjectedSurplusAtExpiry é null quando não se aplica
/// (ExpiresToday, sem validade, já vencido, ou validade bloqueada).
/// Nunca representa perda ou prejuízo.
/// </summary>
public sealed class InventoryProjectionLotResult
{
    public int LotId { get; init; }
    public InventoryProjectionLotKind Kind { get; init; }
    public double Quantity { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public int? DaysUntilExpiry { get; init; }
    public bool AlreadyExpired { get; init; }
    public double? ProjectedSurplusAtExpiry { get; init; }
    public double? ProjectedSurplusValue { get; init; }
}

/// <summary>Resultado determinístico da projeção 70D-B1. SKU e validade separados.</summary>
public sealed class InventoryProjectionResult
{
    public InventorySkuProjectionBlockedReason SkuBlockedReason { get; init; }
    public InventoryExpiryProjectionBlockedReason ExpiryBlockedReason { get; init; }
    public bool CanProjectSku => SkuBlockedReason == InventorySkuProjectionBlockedReason.None;
    public bool CanProjectExpiry => ExpiryBlockedReason == InventoryExpiryProjectionBlockedReason.None;
    public int HorizonDays { get; init; }
    public double? ProjectedDemand { get; init; }
    public double? ProjectedExcessQuantity { get; init; }
    public double TrackedLotQuantity { get; init; }
    public double UntrackedWarehouseQuantity { get; init; }

    /// <summary>
    /// Fato: há estoque na geladeira, fora do rastreio por lote.
    /// Não afirma consumo.
    /// </summary>
    public bool HasLotLocationLimitation { get; init; }

    public IReadOnlyList<InventoryProjectionLotResult> Lots { get; init; } = [];
}
