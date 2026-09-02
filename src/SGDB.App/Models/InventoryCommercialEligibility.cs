namespace SGDB.Models;

/// <summary>
/// Resultado A–D da elegibilidade comercial 70F-B1.
/// CommercialCandidate não é promoção, desconto nem execução.
/// </summary>
public enum InventoryCommercialEligibilityKind
{
    /// <summary>A — há tese para análise comercial futura. Não executa.</summary>
    CommercialCandidate = 0,
    /// <summary>B — monitoramento/priorização operacional. Sem autorização de promoção.</summary>
    MonitorOnly,
    /// <summary>C — conferir dados antes de análise comercial.</summary>
    ReviewData,
    /// <summary>D — não emitir recomendação comercial.</summary>
    NoCommercialRecommendation,
}

/// <summary>
/// Motivo atômico 70F-B1. Sem texto PT-BR. Sem preço, margem ou execução.
/// </summary>
public enum InventoryCommercialEligibilityReason
{
    None = 0,
    Expired,
    InvalidInput,
    NegativeStock,
    NegativeLocationStock,
    NegativeWarehouseStock,
    InconsistentStockTotals,
    TrackedQuantityExceedsWarehouse,
    DuplicateLotId,
    InvalidLotQuantity,
    InvalidExpiryDate,
    ProjectionMissing,
    DuplicateProjection,
    CompositionProduct,
    InsufficientHistory,
    NoPhysicalEvidence,
    LocationLimitation,
    Undated,
    NoLot,
    AnalysisUnavailable,
    ExpiresToday,
    NearExpiryWithoutSurplus,
    DatedWithoutSurplusInWindow,
    ProjectedExpirySurplus,
    ProjectedExcess,
    Idle,
    HighCoverageWithoutExcess,
    NoObservableDemand,
    ZeroStock,
}

/// <summary>
/// Classificação pura 70F-B1. Não recopia VMV/projeção. Não sobrescreve
/// <see cref="InventoryOperatorAction"/>. Sem quantidade financeira.
/// </summary>
public sealed class InventoryCommercialEligibilityResult
{
    public int ProductId { get; init; }
    public InventoryCommercialEligibilityKind Kind { get; init; } =
        InventoryCommercialEligibilityKind.NoCommercialRecommendation;
    public InventoryCommercialEligibilityReason PrimaryReason { get; init; }
    public IReadOnlyList<InventoryCommercialEligibilityReason> SecondaryReasons { get; init; } = [];
    public InventoryAttentionConfidence Confidence { get; init; } =
        InventoryAttentionConfidence.Unavailable;
}
