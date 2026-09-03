namespace SGDB.Models;

/// <summary>
/// Status 70F-B5B da orientação comercial. Suggested exige B4 Available.
/// Não é promoção ativa, PDV nem ação reservada da Central de Validades.
/// </summary>
public enum InventoryPromotionSuggestionStatus
{
    Suggested = 0,
    MonitorOnly,
    ReviewData,
    NotApplicable,
    PolicyMissing,
    PolicyInvalid,
    FinancialDataUnavailable,
    Expired,
}

/// <summary>
/// Ação 70F-B5B. ConsiderPromotion não executa nem grava preço.
/// </summary>
public enum InventoryPromotionSuggestionAction
{
    None = 0,
    ConsiderPromotion,
    PrioritizeExposure,
    Monitor,
    ReviewData,
    RemoveExpired,
}

/// <summary>
/// Objetivo derivado da tese B4. Sem texto PT-BR.
/// </summary>
public enum InventoryPromotionSuggestionObjective
{
    None = 0,
    ReduceProjectedExpirySurplus,
    ReduceProjectedExcess30,
    IncreaseCommercialAttention,
    MonitorTurnover,
    ReviewInformation,
    RemoveExpired,
}

/// <summary>
/// Motivo atômico B5. Sem copy de UI. Sem score.
/// </summary>
public enum InventoryPromotionSuggestionReason
{
    None = 0,
    InvalidInput,
    ScenarioMissing,
    DuplicateScenario,
    Expired,
    LocationLimitation,
    ReviewData,
    LimitedConfidence,
    UnavailableConfidence,
    ExpiresToday,
    NearExpiryWithoutSurplus,
    DatedWithoutSurplusInWindow,
    IdleOnly,
    HighCoverageOnly,
    PolicyMissing,
    PolicyInvalid,
    MissingProduct,
    UnknownCost,
    InvalidCost,
    NotSellable,
    CompositionProduct,
    AmbiguousSaleUnit,
    FinancialDataUnavailable,
    NotApplicable,
    SuggestedBecauseExpirySurplus,
    SuggestedBecauseProjectedExcess,
}

/// <summary>
/// Aviso atômico B5. Não é motivo principal. Sem PT-BR.
/// </summary>
public enum InventoryPromotionSuggestionWarning
{
    MinimumMarginPolicyAllowsAtCost = 0,
    WholesalePricingMayDiffer,
}

/// <summary>
/// Entrada mínima: autoridade B4 + contexto opcional já conhecido.
/// Sem custo, VMV, estoque ou recálculo financeiro.
/// </summary>
public sealed class InventoryPromotionSuggestionInput
{
    public InventoryCommercialScenarioResult? Scenario { get; init; }
    public InventoryAttentionPriority? AttentionPriority { get; init; }
    public bool HasWholesalePricing { get; init; }
}

/// <summary>
/// Resultado puro 70F-B5B. Scenarios são os objetos B4, sem recálculo.
/// </summary>
public sealed class InventoryPromotionSuggestionResult
{
    public int ProductId { get; init; }
    public InventoryPromotionSuggestionStatus Status { get; init; } =
        InventoryPromotionSuggestionStatus.NotApplicable;
    public InventoryPromotionSuggestionAction Action { get; init; }
    public InventoryCommercialScenarioThesis Thesis { get; init; }
    public InventoryPromotionSuggestionObjective Objective { get; init; }
    public InventoryAttentionConfidence Confidence { get; init; } =
        InventoryAttentionConfidence.Unavailable;
    public InventoryAttentionPriority? AttentionPriority { get; init; }
    public InventoryPromotionSuggestionReason PrimaryReason { get; init; }
    public IReadOnlyList<InventoryPromotionSuggestionReason> SecondaryReasons { get; init; } = [];
    public IReadOnlyList<InventoryPromotionSuggestionWarning> Warnings { get; init; } = [];
    public double? AttentionQuantity { get; init; }
    public InventoryCommercialAttentionQuantitySource AttentionQuantitySource { get; init; }
    public IReadOnlyList<InventoryCommercialScenario> Scenarios { get; init; } = [];
}

/// <summary>
/// Entrada 70F-B5C. População = Intelligence.Rows. Sem I/O.
/// </summary>
public sealed class InventoryPromotionSuggestionComposeInput
{
    public InventoryIntelligenceSnapshot? Intelligence { get; init; }
    public InventoryCommercialScenarioSnapshot? Scenarios { get; init; }
}

/// <summary>
/// Linha composta 70F-B5C. ProductId é o da autoridade 70C.
/// </summary>
public sealed class InventoryPromotionSuggestionRow
{
    public int ProductId { get; init; }
    public InventoryPromotionSuggestionResult Suggestion { get; init; } = new();
}

/// <summary>
/// Snapshot em lote 70F-B5C. Rows na ordem de Intelligence.Rows.
/// QueryCount documenta o pipeline (9); o composer não executa query.
/// </summary>
public sealed class InventoryPromotionSuggestionSnapshot
{
    public int QueryCount { get; init; }
    public IReadOnlyList<InventoryPromotionSuggestionRow> Rows { get; init; } = [];
    public IReadOnlyDictionary<int, InventoryPromotionSuggestionRow> ByProductId { get; init; } =
        new Dictionary<int, InventoryPromotionSuggestionRow>();
}
