namespace SGDB.Models;

/// <summary>
/// Ação principal visível ao operador. Não executa. Não cria segundo motor:
/// é tradução do pipeline 70E/70F/70G/71A/B7 já calculado.
/// </summary>
public enum InventorySmartPrincipalAction
{
    ReviewData = 0,
    RemoveExpired,
    BuyNow,
    BuySoon,
    DoNotBuy,
    SuspendPurchase,
    PromoteIndividual,
    HighlightWithoutDiscount,
    KeepPrice,
    EvaluateCombo,
    UnsafeLiquidation,
    Monitor,
    Preserve,
    NoSafePromotion,
}

public enum InventorySmartUrgency
{
    None = 0,
    Attention,
    High,
    Critical,
}

public enum InventorySmartArea
{
    Urgent = 0,
    Buy,
    SellFaster,
    FixData,
    Preserve,
}

public enum InventorySmartViewMode
{
    Simple = 0,
    Detailed,
}

public enum InventoryDataFixLocation
{
    None = 0,
    ProductCadastro,
    StockAdjust,
    LotsAndExpiry,
    Purchases,
    Inventory,
    PackFactor,
}

public enum InventoryComboRejectionReason
{
    InsufficientStock = 0,
    MarginBelowMinimum,
    MissingCost,
    MissingPrice,
    IncompatibleValidity,
    CompanionShortageRisk,
    NoCommercialRelation,
    IndividualPromotionSafer,
    InsufficientData,
    BelowCost,
    NotSellable,
    InactiveOrUnsellable,
    CommerciallyIncompatible,
}

/// <summary>
/// Cartão compacto da visão simples. Textos já traduzidos. Sem I/O.
/// </summary>
public sealed class InventorySmartCard
{
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductTitle { get; init; } = "";
    public InventorySmartPrincipalAction PrincipalAction { get; init; }
    public string ActionText { get; init; } = "";
    public string QuantityOrDeadlineText { get; init; } = "";
    public string ReasonText { get; init; } = "";
    public InventorySmartUrgency Urgency { get; init; }
    public string UrgencyText { get; init; } = "";
    public InventorySmartArea Area { get; init; }
    public string Tone { get; init; } = "";
    public string FixLocationText { get; init; } = "";
    public IReadOnlyList<string> Alternatives { get; init; } = [];
    public IReadOnlyList<string> RejectionReasons { get; init; } = [];
}

/// <summary>
/// Recomendação unificada por produto. Consome autoridades existentes.
/// Uma ação principal; alternativas só para detalhe.
/// </summary>
public sealed class InventorySmartRecommendation
{
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public InventorySmartPrincipalAction PrincipalAction { get; init; }
    public InventorySmartArea Area { get; init; }
    public InventorySmartUrgency Urgency { get; init; }
    public InventoryAttentionConfidence Confidence { get; init; } =
        InventoryAttentionConfidence.Unavailable;

    public string ActionText { get; init; } = "";
    public string ReasonText { get; init; } = "";
    public string QuantityOrDeadlineText { get; init; } = "";
    public string FixLocationText { get; init; } = "";
    public InventoryDataFixLocation FixLocation { get; init; }

    public double? CurrentStock { get; init; }
    public double? RecommendedQuantity { get; init; }
    public double? PackFactor { get; init; }
    public int? PackCount { get; init; }
    public double? EstimatedCost { get; init; }
    public double? CoverageAfterPurchaseDays { get; init; }
    public DateOnly? RecommendedOrderDate { get; init; }
    public DateOnly? ReevaluationDate { get; init; }

    public double? SuggestedPromoPrice { get; init; }
    public double? DiscountAmount { get; init; }
    public double? DiscountPercent { get; init; }
    public double? ResultingMarginPercent { get; init; }
    public int? PromoDurationDays { get; init; }

    public bool HasSafePromotion { get; init; }
    public bool HasSafeCombo { get; init; }
    public bool AnalysisPartial { get; init; }
    public bool AnalysisUnavailable { get; init; }

    public IReadOnlyList<string> Alternatives { get; init; } = [];
    public IReadOnlyList<InventoryComboRejectionReason> ComboRejections { get; init; } = [];
    public IReadOnlyList<string> DataQualityReasons { get; init; } = [];
}

/// <summary>Snapshot de cartões. QueryCount = 0. Ordem = autoridade de origem.</summary>
public sealed class InventorySmartRecommendationSnapshot
{
    public const int OwnQueryCount = 0;
    public int QueryCount { get; init; }
    public InventorySmartViewMode ViewMode { get; init; }
    public IReadOnlyList<InventorySmartRecommendation> Rows { get; init; } = [];
    public IReadOnlyDictionary<int, InventorySmartRecommendation> ByProductId { get; init; } =
        new Dictionary<int, InventorySmartRecommendation>();
}
