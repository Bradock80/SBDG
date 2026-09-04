using SGDB.Domain.Common;

namespace SGDB.Domain.Commercial;

/// <summary>
/// Motor puro 71B-B1: calendário civil, progresso, ritmo, projeção linear e status.
/// Sem SQL, settings, vendas, CMV, UI ou relógio global. QueryCount = 0.
/// </summary>
public static class CommercialGoalProgressEngine
{
    public const int ExpectedQueryCount = 0;

    public const string LinearProjectionSemantics =
        "Projeção linear pelo ritmo atual. Extrapolação por dias civis; não é previsão.";

    public const string PartialDayLimitation =
        "O progresso esperado usa o dia civil inteiro da data de referência; "
        + "o horário do dia não entra no cálculo V1.";

    public static CommercialGoalProgressSnapshot Evaluate(
        CommercialCompetence competence,
        DateOnly referenceDate,
        decimal? goal,
        decimal realized)
    {
        var period = competence.Classify(referenceDate);
        var (elapsed, remaining) = CalendarDays(competence, period, referenceDate);
        if (goal is not decimal value || value == 0m)
        {
            return Base(
                competence, referenceDate, period, elapsed, remaining, goal, realized,
                status: CommercialGoalStatus.NoGoal, hasValidGoal: false);
        }

        if (value < 0m)
        {
            return Base(
                competence, referenceDate, period, elapsed, remaining, goal, realized,
                status: CommercialGoalStatus.InvalidGoal, hasValidGoal: false);
        }

        var validGoal = value;

        if (period == CommercialGoalPeriodState.Future)
        {
            return Base(
                competence, referenceDate, period, elapsed, remaining, validGoal, realized,
                status: CommercialGoalStatus.NotStarted,
                hasValidGoal: true,
                remainingAmount: validGoal,
                expected: 0m);
        }

        var remainingAmount = validGoal - realized;
        if (remainingAmount < 0m)
            remainingAmount = 0m;

        var achievementRatio = realized / validGoal;
        var expected = period == CommercialGoalPeriodState.Closed
            ? validGoal
            : validGoal * elapsed / competence.DaysInMonth;

        decimal? requiredPace = null;
        var hasRequiredPace = false;
        if (period == CommercialGoalPeriodState.Current)
        {
            hasRequiredPace = true;
            requiredPace = remaining == 0
                ? 0m
                : remainingAmount / remaining;
        }

        decimal? projection = period == CommercialGoalPeriodState.Closed
            ? realized
            : realized / elapsed * competence.DaysInMonth;

        var status = ResolveStatus(realized, validGoal, expected);

        return new CommercialGoalProgressSnapshot
        {
            Competence = competence,
            ReferenceDate = referenceDate,
            PeriodState = period,
            DaysInMonth = competence.DaysInMonth,
            ElapsedCalendarDays = elapsed,
            RemainingCalendarDaysIncludingToday = remaining,
            Goal = validGoal,
            Realized = realized,
            RemainingAmount = remainingAmount,
            AchievementRatio = achievementRatio,
            ExpectedLinearProgressAmount = expected,
            RequiredGrossProfitPerRemainingDay = requiredPace,
            ProjectedMonthEndGrossProfit = projection,
            Status = status,
            HasValidGoal = true,
            HasRequiredPace = hasRequiredPace,
            HasLinearProjection = true,
        };
    }

    static CommercialGoalProgressSnapshot Base(
        CommercialCompetence competence,
        DateOnly referenceDate,
        CommercialGoalPeriodState period,
        int elapsed,
        int remaining,
        decimal? goal,
        decimal realized,
        CommercialGoalStatus status,
        bool hasValidGoal,
        decimal? remainingAmount = null,
        decimal? expected = null) =>
        new()
        {
            Competence = competence,
            ReferenceDate = referenceDate,
            PeriodState = period,
            DaysInMonth = competence.DaysInMonth,
            ElapsedCalendarDays = elapsed,
            RemainingCalendarDaysIncludingToday = remaining,
            Goal = goal,
            Realized = realized,
            RemainingAmount = remainingAmount,
            ExpectedLinearProgressAmount = expected,
            Status = status,
            HasValidGoal = hasValidGoal,
            HasRequiredPace = false,
            HasLinearProjection = false,
        };

    static (int Elapsed, int Remaining) CalendarDays(
        CommercialCompetence competence,
        CommercialGoalPeriodState period,
        DateOnly referenceDate)
    {
        var days = competence.DaysInMonth;
        return period switch
        {
            CommercialGoalPeriodState.Future => (0, days),
            CommercialGoalPeriodState.Closed => (days, 0),
            _ => (referenceDate.Day, days - referenceDate.Day + 1),
        };
    }

    static CommercialGoalStatus ResolveStatus(decimal realized, decimal goal, decimal expected)
    {
        if (RoundMoney(realized) >= RoundMoney(goal))
            return CommercialGoalStatus.Achieved;

        var realizedCents = RoundMoney(realized);
        var expectedCents = RoundMoney(expected);
        if (realizedCents > expectedCents)
            return CommercialGoalStatus.AbovePace;
        if (realizedCents == expectedCents)
            return CommercialGoalStatus.OnPace;
        return CommercialGoalStatus.BelowPace;
    }

    /// <summary>
    /// Mesma política de <see cref="MonetaryRounding"/>: 2 casas, AwayFromZero.
    /// Usada só na comparação de status; as métricas permanecem em precisão cheia.
    /// </summary>
    public static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
