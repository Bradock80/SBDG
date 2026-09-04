namespace SGDB.Domain.Commercial;

public enum CommercialGoalPeriodState
{
    Future = 0,
    Current,
    Closed,
}

/// <summary>
/// Status determinístico V1. Sem limiar “crítico”. Comparação de ritmo por centavos.
/// </summary>
public enum CommercialGoalStatus
{
    NoGoal = 0,
    InvalidGoal,
    NotStarted,
    Achieved,
    AbovePace,
    OnPace,
    BelowPace,
}

/// <summary>
/// Snapshot puro 71B-B1. QueryCount = 0. Null em métricas significa N/A, nunca 0.
/// </summary>
public sealed class CommercialGoalProgressSnapshot
{
    public const int ExpectedQueryCount = 0;

    public CommercialCompetence Competence { get; init; }
    public DateOnly ReferenceDate { get; init; }
    public CommercialGoalPeriodState PeriodState { get; init; }
    public int DaysInMonth { get; init; }
    public int ElapsedCalendarDays { get; init; }
    public int RemainingCalendarDaysIncludingToday { get; init; }

    public decimal? Goal { get; init; }
    public decimal Realized { get; init; }
    public decimal? RemainingAmount { get; init; }
    public decimal? AchievementRatio { get; init; }

    public decimal? ExpectedLinearProgressAmount { get; init; }
    public decimal? RequiredGrossProfitPerRemainingDay { get; init; }
    public decimal? ProjectedMonthEndGrossProfit { get; init; }

    public CommercialGoalStatus Status { get; init; }

    public bool HasValidGoal { get; init; }
    public bool HasRequiredPace { get; init; }
    public bool HasLinearProjection { get; init; }

    public string LinearProjectionSemantics { get; init; } =
        CommercialGoalProgressEngine.LinearProjectionSemantics;
    public string PartialDayLimitation { get; init; } =
        CommercialGoalProgressEngine.PartialDayLimitation;
}
