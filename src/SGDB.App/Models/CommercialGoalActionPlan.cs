using SGDB.Domain.Commercial;

namespace SGDB.Models;

/// <summary>
/// Tipo qualitativo B7. Reusa a gravidade 70E; ProtectAvailability vem de 70G.
/// Promoção e combo nunca são tipo primário — entram como complemento.
/// </summary>
public enum CommercialGoalActionType
{
    ReviewData = 0,
    RemoveExpired,
    PrioritizeExpiryRisk,
    PrioritizeExcess,
    PrioritizeIdle,
    ProtectAvailability,
    Monitor,
}

/// <summary>
/// Origem primária da recomendação. O operador precisa ver por que o item apareceu.
/// </summary>
public enum CommercialGoalActionOrigin
{
    InventoryAttention = 0,
    PurchaseGuidance,
}

/// <summary>
/// Fontes que contribuíram para o item consolidado.
/// </summary>
[Flags]
public enum CommercialGoalActionSource
{
    None = 0,
    InventoryAttention = 1 << 0,
    PromotionSuggestion = 1 << 1,
    PurchaseGuidance = 1 << 2,
    SmartCombo = 1 << 3,
}

/// <summary>
/// Como o plano se relaciona com a meta. Não altera fatos de estoque.
/// </summary>
public enum CommercialGoalActionPlanMode
{
    Operational = 0,
    InventoryOnly,
    FutureCompetence,
}

/// <summary>
/// Limitações estruturadas B7. Sem texto PT-BR.
/// </summary>
[Flags]
public enum CommercialGoalActionLimitation
{
    None = 0,
    LegacyCostEstimate = 1 << 0,
    LocationLimitation = 1 << 1,
    InsufficientHistory = 1 << 2,
    NoPhysicalEvidence = 1 << 3,
    StructuralDataIssue = 1 << 4,
    FinancialUnavailable = 1 << 5,
}

/// <summary>
/// Item consolidado por ProductId. Sem contribuição da meta, demanda inventada ou valor incremental.
/// </summary>
public sealed class CommercialGoalActionItem
{
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";

    public CommercialGoalActionType ActionType { get; init; }
    public InventoryAttentionReason AttentionReason { get; init; }
    public InventoryAttentionPriority Priority { get; init; } = InventoryAttentionPriority.Normal;
    public InventoryAttentionConfidence Confidence { get; init; } =
        InventoryAttentionConfidence.Unavailable;

    public CommercialGoalActionOrigin Source { get; init; }
    public CommercialGoalActionSource Sources { get; init; }

    public double? CurrentStock { get; init; }
    public double? CoverageDays { get; init; }
    public double? ProjectedExcess { get; init; }
    public double? ProjectedExpirySurplus { get; init; }
    public int? DaysWithoutSale { get; init; }
    public int? NearestDatedDaysUntilExpiry { get; init; }

    public bool HasPromotionSuggestion { get; init; }
    public bool HasComboSuggestion { get; init; }
    public int ComboSuggestionCount { get; init; }
    public InventoryPurchaseGuidanceAction PurchaseGuidanceAction { get; init; }

    public CommercialGoalActionLimitation Limitations { get; init; }
}

/// <summary>
/// Snapshots já carregados. O composer B7 não executa I/O.
/// </summary>
public sealed class CommercialGoalActionPlanSources
{
    public InventoryIntelligenceSnapshot? Intelligence { get; init; }
    public InventoryAttentionSnapshot? Attention { get; init; }
    public InventoryPromotionSuggestionSnapshot? Promotion { get; init; }
    public InventoryPurchaseGuidanceSnapshot? Guidance { get; init; }
    public InventoryComboIntelligenceSnapshot? Combos { get; init; }
    public int QueryCount { get; init; }
}

/// <summary>
/// Plano qualitativo 71B-B7. OwnQueryCount = 0. Máximo 5 ações.
/// </summary>
public sealed class CommercialGoalActionPlanSnapshot
{
    public const int OwnQueryCount = 0;
    public const int MaxActions = 5;

    public CommercialCompetence Competence { get; init; }
    public DateOnly ReferenceDate { get; init; }
    public CommercialGoalStatus? GoalStatus { get; init; }
    public CommercialGoalCostQuality FinancialQuality { get; init; }
    public CommercialGoalProgressSkipReason ProgressSkipReason { get; init; }
    public bool HasValidGoal { get; init; }
    public CommercialGoalActionPlanMode Mode { get; init; }
    public IReadOnlyList<CommercialGoalActionItem> Items { get; init; } = [];
    public CommercialGoalActionLimitation Limitations { get; init; }
    public int CandidateCount { get; init; }
    public int QueryCount { get; init; }

    public bool HasLimitation(CommercialGoalActionLimitation limitation) =>
        Limitations.HasFlag(limitation);
}
