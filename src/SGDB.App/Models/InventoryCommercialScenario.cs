namespace SGDB.Models;

/// <summary>
/// Status 70F-B4B do simulador de catálogo. Available exige ao menos um cenário.
/// Sem execução, promoção ou preço de PDV.
/// </summary>
public enum InventoryCommercialScenarioStatus
{
    Available = 0,
    MonitorOnly,
    ReviewData,
    NoRecommendation,
    PolicyMissing,
    PolicyInvalid,
    FinancialDataUnavailable,
    Expired,
}

/// <summary>
/// Tese que dirige quantidade e, se as portas passarem, os números.
/// ExpirySurplus tem precedência sobre ProjectedExcess30 sobre Idle.
/// </summary>
public enum InventoryCommercialScenarioThesis
{
    None = 0,
    ProjectedExcess30,
    ExpirySurplus,
    Idle,
    HighCoverage,
}

/// <summary>
/// Origem da quantidade em atenção. Projeção, não ordem de venda.
/// </summary>
public enum InventoryCommercialAttentionQuantitySource
{
    None = 0,
    ProjectedExcess30,
    ExpirySurplus,
}

public enum InventoryCommercialScenarioKind
{
    Light = 0,
    Moderate,
}

/// <summary>
/// Motivo atômico 70F-B4B. Sem texto PT-BR. Sem desconto aplicado.
/// </summary>
public enum InventoryCommercialScenarioReason
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
    LocationLimitation,
    InsufficientHistory,
    NoPhysicalEvidence,
    Undated,
    NoLot,
    ReviewData,
    ExpiresToday,
    NearExpiryWithoutSurplus,
    DatedWithoutSurplusInWindow,
    HighCoverageMonitoring,
    LimitedConfidence,
    UnavailableConfidence,
    Idle,
    PolicyMissing,
    PolicyInvalid,
    MissingProduct,
    UnknownCost,
    InvalidCost,
    UnusablePrice,
    InvalidPrice,
    NotSellable,
    CompositionProduct,
    AmbiguousSaleUnit,
    FloorUnavailable,
    PriceBelowFloor,
    PriceAtFloor,
    NoFinancialRoom,
    ScenarioCollapsedByRounding,
    ExpirySurplus,
    ProjectedExcess30,
    NoRecommendation,
}

/// <summary>
/// Um preço simulado de catálogo. Não é promoção nem valor de PDV.
/// </summary>
public sealed class InventoryCommercialScenario
{
    public InventoryCommercialScenarioKind Kind { get; init; }
    public double SimulatedCatalogPrice { get; init; }
    public double ReductionAmount { get; init; }
    public double ReductionPercent { get; init; }
    public double GrossMarginPercent { get; init; }
}

/// <summary>
/// Resultado puro 70F-B4B. Scenarios vazio quando não há simulação numérica.
/// CurrentCatalogPrice é catálogo B2, não total de caixa.
/// </summary>
public sealed class InventoryCommercialScenarioResult
{
    public int ProductId { get; init; }
    public InventoryCommercialScenarioStatus Status { get; init; } =
        InventoryCommercialScenarioStatus.NoRecommendation;
    public InventoryCommercialScenarioReason PrimaryReason { get; init; }
    public IReadOnlyList<InventoryCommercialScenarioReason> SecondaryReasons { get; init; } = [];
    public InventoryCommercialScenarioThesis Thesis { get; init; }
    public InventoryAttentionConfidence Confidence { get; init; } =
        InventoryAttentionConfidence.Unavailable;
    public double? CurrentCatalogPrice { get; init; }
    public double? CurrentGrossMarginPercent { get; init; }
    public double? MinimumAllowedCatalogPrice { get; init; }
    public double? MinimumGrossMarginPercent { get; init; }
    public double? FinancialRoomAmount { get; init; }
    public bool CatalogPriceIsAboveMinimumAllowed { get; init; }
    public double? AttentionQuantity { get; init; }
    public InventoryCommercialAttentionQuantitySource AttentionQuantitySource { get; init; }
    public IReadOnlyList<InventoryCommercialScenario> Scenarios { get; init; } = [];
}

/// <summary>
/// Entrada já calculada. Sem I/O. B4C monta este DTO depois.
/// </summary>
public sealed class InventoryCommercialScenarioInput
{
    public InventoryCommercialEligibilityResult? Eligibility { get; init; }
    public InventoryCommercialFacts? Facts { get; init; }
    public InventoryCommercialMarginPolicyResolution? PolicyResolution { get; init; }
    public InventoryCommercialPriceFloorResult? Floor { get; init; }
    public ProductTurnoverRow? Turnover { get; init; }
    public InventoryProjectedProduct? Projection { get; init; }
    public InventoryAttentionResult? Attention { get; init; }
}

/// <summary>
/// Entrada já carregada 70F-B4C. Sem I/O. Policy vem resolvida; B3/B4B correm em memória.
/// ProjectionRows, se informado, permite detectar DuplicateProjection como a 70E.
/// </summary>
public sealed record InventoryCommercialScenarioComposeInput
{
    public InventoryIntelligenceSnapshot? Intelligence { get; init; }
    public InventoryProjectionSnapshot? Projection { get; init; }
    public IReadOnlyList<InventoryProjectedProduct>? ProjectionRows { get; init; }
    public InventoryAttentionSnapshot? Attention { get; init; }
    public IReadOnlyList<InventoryCommercialEligibilityResult>? Eligibility { get; init; }
    public InventoryCommercialFactsSnapshot? Facts { get; init; }
    public InventoryCommercialMarginPolicyResolution? PolicyResolution { get; init; }
}

/// <summary>
/// Linha composta 70F-B4C. ProductId é o da autoridade 70C.
/// PolicyResolution é global no snapshot, não copiada aqui.
/// ScenarioResult permanece autoridade de status.
/// </summary>
public sealed class InventoryCommercialScenarioRow
{
    public int ProductId { get; init; }
    public ProductTurnoverRow Turnover { get; init; } = new();
    public InventoryProjectedProduct? Projection { get; init; }
    public InventoryAttentionResult Attention { get; init; } = new();
    public InventoryCommercialEligibilityResult Eligibility { get; init; } = new();
    public InventoryCommercialFacts Facts { get; init; } = new();
    public InventoryCommercialPriceFloorResult PriceFloor { get; init; } = new();
    public InventoryCommercialScenarioResult ScenarioResult { get; init; } = new();
}

/// <summary>
/// Snapshot composto 70F-B4C. Rows na ordem de Intelligence.Rows.
/// QueryCount documenta o pipeline (9); o composer não executa query.
/// </summary>
public sealed class InventoryCommercialScenarioSnapshot
{
    public int QueryCount { get; init; }
    public InventoryCommercialMarginPolicyResolution PolicyResolution { get; init; } = new();
    public IReadOnlyList<InventoryCommercialScenarioRow> Rows { get; init; } = [];
    public IReadOnlyDictionary<int, InventoryCommercialScenarioRow> ByProductId { get; init; } =
        new Dictionary<int, InventoryCommercialScenarioRow>();
}
