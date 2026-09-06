using SGDB.Domain.Commercial;

namespace SGDB.Models;

/// <summary>
/// Estado do envelope 71C. Não é ação por produto e não substitui
/// <see cref="CommercialGoalActionType"/> nem <see cref="CommercialGoalActionPlanMode"/>.
/// InventoryOnly da meta permanece nos snapshots 71B anexados.
/// </summary>
public enum CentralDecisionState
{
    Operational = 0,
    Limited,
    Empty,
    Unavailable,
    Future,
}

/// <summary>
/// Degradação do envelope 71C. Não duplica gravidade 70E/B7 nem qualidade B8.
/// Motivos por item continuam em <see cref="CommercialGoalActionItem.Limitations"/>.
/// </summary>
[Flags]
public enum CentralDecisionLimitation
{
    None = 0,
    PromotionSourceUnavailable = 1 << 0,
    ComboSourceUnavailable = 1 << 1,
    ContributionUnavailable = 1 << 2,
    IntelligenceUnavailable = 1 << 3,
}

/// <summary>
/// Item da fila Agir agora. Compõe o item B7; não copia autoridade de ação.
/// Contribuição B8 é contexto histórico opcional, nunca recomendação.
/// </summary>
public sealed class CentralDecisionActNowItem
{
    public required CommercialGoalActionItem Action { get; init; }
    public CommercialGoalProductContributionRow? Contribution { get; init; }

    public int ProductId => Action.ProductId;
    public CommercialGoalActionType ActionType => Action.ActionType;
}

/// <summary>
/// Zona informativa Preservar. Não é ação primária e não é ProtectAvailability.
/// A seleção (cobertura Normal, cap 3) pertence à B2.
/// </summary>
public sealed class CentralDecisionPreserveItem
{
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public InventoryCoverageBand CoverageBand { get; init; }
    public CommercialGoalProductContributionRow? Contribution { get; init; }
}

/// <summary>
/// Contrato de saída 71C-B1. Envelope sobre 71B/B7/B8. 0 SQL, 0 I/O, sem ranking.
/// </summary>
public sealed class CentralDecisionSnapshot
{
    public const int OwnQueryCount = 0;
    public const int MaxActNowItems = CommercialGoalActionPlanSnapshot.MaxActions;
    public const int MaxPreserveItems = 3;

    public CentralDecisionState State { get; init; }
    public CommercialCompetence Competence { get; init; }
    public DateOnly ReferenceDate { get; init; }

    /// <summary>Snapshot 71B-B4. Faixa compacta Meta/Realizado/Falta/Status lê daqui.</summary>
    public CommercialGoalSnapshot? Goal { get; init; }

    /// <summary>Plano B7 anexado. A fila Agir agora não substitui este snapshot.</summary>
    public CommercialGoalActionPlanSnapshot? ActionPlan { get; init; }

    /// <summary>B8 histórico opcional. Null = contexto financeiro ausente, não zero.</summary>
    public CommercialGoalProductContributionSnapshot? Contribution { get; init; }

    public IReadOnlyList<CentralDecisionActNowItem> ActNow { get; init; } = [];
    public IReadOnlyList<CentralDecisionPreserveItem> Preserve { get; init; } = [];

    public CentralDecisionLimitation Limitations { get; init; }
    public int QueryCount { get; init; }

    public decimal? GoalAmount => Goal?.GoalAmount;
    public decimal? RealizedGrossProfit => Goal?.RealizedGrossProfit;
    public decimal? RemainingAmount => Goal?.Progress?.RemainingAmount;
    public CommercialGoalStatus? GoalStatus => Goal?.Status;
    public CommercialGoalActionPlanMode? PlanMode => ActionPlan?.Mode;
    public CommercialGoalCostQuality? FinancialQuality => Goal?.FinancialQuality;
    public CommercialGoalProgressSkipReason ProgressSkipReason =>
        Goal?.ProgressSkipReason ?? CommercialGoalProgressSkipReason.None;

    public bool HasLimitation(CentralDecisionLimitation limitation) =>
        Limitations.HasFlag(limitation);
}
