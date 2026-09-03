namespace SGDB.Models;

/// <summary>
/// Status qualitativo 70G-B1 da orientação de reposição.
/// GuidanceAvailable cobre ConsiderReplenishment e DoNotReplenishNow.
/// Não é pedido, quantidade nem tela.
/// </summary>
public enum InventoryPurchaseGuidanceStatus
{
    GuidanceAvailable = 0,
    Monitor,
    ReviewData,
    NotApplicable,
}

/// <summary>
/// Ação qualitativa 70G-B1. ConsiderReplenishment não gera ordem.
/// Proibido: Replenish, Buy, BuyNow, OrderNow.
/// </summary>
public enum InventoryPurchaseGuidanceAction
{
    None = 0,
    ConsiderReplenishment,
    DoNotReplenishNow,
    Monitor,
    ReviewData,
}

/// <summary>
/// Motivo atômico 70G-B1. None é sentinela residual (Attention/Normal isolados),
/// não tese comercial.
/// </summary>
public enum InventoryPurchaseGuidanceReason
{
    None = 0,
    OutOfStockWithObservedDemand,
    CriticalCoverage,
    LowCoverage,
    ProjectedExcess30,
    ProjectedExpirySurplus,
    IdleStock,
    NoObservableDemand,
    InsufficientHistory,
    NoPhysicalEvidence,
    StructuralDataIssue,
    LocationLimitation,
    CompositionProduct,
    Expired,
    ExpiresToday,
}

/// <summary>
/// Fatos já calculados por 70C/70D. Contrato mínimo, sem recálculo.
/// </summary>
public sealed class InventoryPurchaseGuidanceInput
{
    public int ProductId { get; init; }

    public double Stock { get; init; }
    public double StockFridge { get; init; }
    public double TotalStock { get; init; }
    public double Vmv30 { get; init; }
    public InventoryCoverageBand CoverageBand { get; init; }
    public double? CoverageDays { get; init; }
    public bool IsIdle { get; init; }
    public bool IsZeroStockWithTurnover { get; init; }
    public bool HasPhysicalAvailabilityEvidence { get; init; }
    public int HistoryDays { get; init; }
    public bool IsHistoryInsufficient30 { get; init; }
    public bool IsCompositionProduct { get; init; }
    public bool HasLocationStockAnomaly { get; init; }

    public bool CanProjectSku { get; init; }
    public double? ProjectedExcessQuantity { get; init; }
    public double? ProjectedExpirySurplus { get; init; }
    public bool HasLotLocationLimitation { get; init; }
    public InventorySkuProjectionBlockedReason SkuBlockedReason { get; init; }
    public InventoryExpiryProjectionBlockedReason ExpiryBlockedReason { get; init; }
    public bool HasExpiredLot { get; init; }
    public bool HasExpiresTodayLot { get; init; }
    public bool HasTrackedQuantityExceedsWarehouse { get; init; }
    public bool HasInvalidLotQuantity { get; init; }
    public bool HasDuplicateLot { get; init; }
    public bool HasInvalidExpiry { get; init; }
    public bool IsInvalidInput { get; init; }
}

/// <summary>
/// Resultado puro 70G-B1. Sem quantidade, fornecedor, preço, margem, pedido ou score.
/// </summary>
public sealed class InventoryPurchaseGuidanceResult
{
    public int ProductId { get; init; }
    public InventoryPurchaseGuidanceStatus Status { get; init; } =
        InventoryPurchaseGuidanceStatus.NotApplicable;
    public InventoryPurchaseGuidanceAction Action { get; init; }
    public InventoryAttentionConfidence Confidence { get; init; } =
        InventoryAttentionConfidence.Unavailable;
    public InventoryPurchaseGuidanceReason PrimaryReason { get; init; }
    public IReadOnlyList<InventoryPurchaseGuidanceReason> SecondaryReasons { get; init; } = [];
}
