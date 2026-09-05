namespace SGDB.Domain.Commercial;

/// <summary>
/// Motivo de não executar B1. Flags independentes: inválido ≠ indisponível.
/// </summary>
[Flags]
public enum CommercialGoalProgressSkipReason
{
    None = 0,
    GrossProfitUnavailable = 1 << 0,
    InvalidGoalConfiguration = 1 << 1,
}

/// <summary>
/// Limitações semânticas V1 da Meta Comercial. Códigos, não textos de UI.
/// </summary>
[Flags]
public enum CommercialGoalLimitation
{
    None = 0,
    LegacyCostEstimate = 1 << 0,
    ExchangesNotAdjusted = 1 << 1,
    LinearCalendarProjection = 1 << 2,
    CurrentDayTreatedAsWholeDay = 1 << 3,
    HistoricalDefaultCanChange = 1 << 4,
}

/// <summary>
/// Snapshot consolidado 71B-B4. Compõe B1/B2/B3; não recalcula receita, CMV, ritmo nem meta.
/// </summary>
public sealed class CommercialGoalSnapshot
{
    public const int OwnQueryCount = 0;

    public CommercialCompetence Competence { get; init; }
    public DateOnly ReferenceDate { get; init; }

    public required CommercialGoalSettingResolution GoalResolution { get; init; }
    public required CommercialGoalFinancialSnapshot Financial { get; init; }
    public CommercialGoalProgressSnapshot? Progress { get; init; }

    public CommercialGoalProgressSkipReason ProgressSkipReason { get; init; }
    public CommercialGoalLimitation Limitations { get; init; }

    /// <summary>
    /// Leituras herdadas: B3 (1 ou 2) + B2 (2). B4 não adiciona SQL.
    /// </summary>
    public int QueryCount { get; init; }

    public CommercialGoalSettingSource GoalSource => GoalResolution.Source;
    public decimal? GoalAmount => GoalResolution.GoalAmount;
    public bool HasValidGoal => GoalResolution.HasValidGoal;

    public CommercialGoalCostQuality FinancialQuality => Financial.CostQuality;
    public bool GrossProfitAvailable => Financial.GrossProfitAvailable;
    public decimal? GrossProfit => Financial.GrossProfit;

    public bool ProgressAvailable => Progress is not null;
    public CommercialGoalStatus? Status => Progress?.Status;
    public decimal? RealizedGrossProfit => Progress?.Realized;

    public bool HasLimitation(CommercialGoalLimitation limitation) =>
        Limitations.HasFlag(limitation);
}
