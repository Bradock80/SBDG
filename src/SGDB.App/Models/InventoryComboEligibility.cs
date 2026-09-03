namespace SGDB.Models;

/// <summary>
/// 71A-B1 — elegível ou bloqueado. Sem ranking, par, preço ou sugestão.
/// </summary>
public enum ComboEligibilityStatus
{
    Eligible = 0,
    Blocked,
}

/// <summary>
/// Motivo de alvo 71A-B1. Teses positivas só depois dos blockers.
/// Coverage crítica/baixa e NearExpiryWithoutSurplus não são tese de giro.
/// </summary>
public enum ComboTargetEligibilityReason
{
    None = 0,
    TargetExpired,
    TargetExpiresToday,
    TargetComposition,
    TargetAmbiguousUnit,
    TargetStockUnsafe,
    TargetNoPhysicalEvidence,
    TargetReviewData,
    TargetZeroWithDemand,
    TargetAnalysisUnavailable,
    TargetNoTurnoverNeed,
    ExpirySurplus,
    ProjectedExcess,
    Idle,
}

/// <summary>
/// Motivo de âncora 71A-B1. Só <see cref="HealthyNormalCoverage"/> é elegível.
/// </summary>
public enum ComboAnchorEligibilityReason
{
    None = 0,
    AnchorComposition,
    AnchorAmbiguousUnit,
    AnchorStockUnsafe,
    AnchorLocationAnomaly,
    AnchorNoPhysicalEvidence,
    AnchorInsufficientHistory,
    AnchorFinancialUnavailable,
    AnchorExpiryUrgent,
    AnchorReviewData,
    AnchorNotApplicable,
    AnchorConsiderReplenishment,
    AnchorDoNotReplenishNow,
    AnchorNoObservableDemand,
    AnchorCoverageUnsafe,
    AnchorUnitGuardrail,
    HealthyNormalCoverage,
}

/// <summary>
/// Entrada pura 71A-B1. Snapshots já calculados por 70C/70E/70F/70G. Sem I/O.
/// </summary>
public sealed class InventoryComboEligibilityInput
{
    public ProductTurnoverRow? Turnover { get; init; }
    public InventoryAttentionResult? Attention { get; init; }
    public InventoryCommercialFacts? Facts { get; init; }
    public InventoryPurchaseGuidanceResult? Guidance { get; init; }
}

/// <summary>Resultado puro: este SKU pode ser o produto que precisa girar?</summary>
public sealed class InventoryComboTargetEligibility
{
    public int ProductId { get; init; }
    public ComboEligibilityStatus Status { get; init; } = ComboEligibilityStatus.Blocked;
    public ComboTargetEligibilityReason Reason { get; init; }
    public InventoryAttentionConfidence Confidence { get; init; } =
        InventoryAttentionConfidence.Unavailable;
}

/// <summary>Resultado puro: este SKU pode ser âncora comercialmente saudável?</summary>
public sealed class InventoryComboAnchorEligibility
{
    public int ProductId { get; init; }
    public ComboEligibilityStatus Status { get; init; } = ComboEligibilityStatus.Blocked;
    public ComboAnchorEligibilityReason Reason { get; init; }
    public InventoryAttentionConfidence Confidence { get; init; } =
        InventoryAttentionConfidence.Unavailable;
}
