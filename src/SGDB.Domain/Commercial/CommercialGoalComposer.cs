namespace SGDB.Domain.Commercial;

/// <summary>
/// Compositor puro 71B-B4. Reúne B3 + B2 + B1 sem fórmula concorrente e sem I/O.
/// Data de referência explícita; sem relógio global.
/// </summary>
public static class CommercialGoalComposer
{
    public const int OwnQueryCount = 0;
    public const int InheritedFinancialQueryCount =
        CommercialGoalFinancialSnapshot.ExpectedQueryCount;

    public static CommercialGoalSnapshot Compose(
        CommercialGoalSettingResolution goal,
        CommercialGoalFinancialSnapshot financial,
        DateOnly referenceDate)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(financial);
        if (goal.Competence != financial.Competence)
        {
            throw new ArgumentException(
                "A competência da resolução da meta deve coincidir com a do snapshot financeiro.",
                nameof(financial));
        }

        var skip = CommercialGoalProgressSkipReason.None;
        if (IsInvalidGoalSource(goal.Source))
            skip |= CommercialGoalProgressSkipReason.InvalidGoalConfiguration;

        var realized = financial.GrossProfitAvailable ? financial.GrossProfit : null;
        if (realized is null)
            skip |= CommercialGoalProgressSkipReason.GrossProfitUnavailable;

        CommercialGoalProgressSnapshot? progress = null;
        if (skip == CommercialGoalProgressSkipReason.None && realized is decimal realizedAmount)
        {
            decimal? engineGoal = goal.HasValidGoal ? goal.GoalAmount : null;
            progress = CommercialGoalProgressEngine.Evaluate(
                financial.Competence,
                referenceDate,
                engineGoal,
                realizedAmount);
        }

        return new CommercialGoalSnapshot
        {
            Competence = financial.Competence,
            ReferenceDate = referenceDate,
            GoalResolution = goal,
            Financial = financial,
            Progress = progress,
            ProgressSkipReason = skip,
            Limitations = ResolveLimitations(goal, financial, progress),
            QueryCount = goal.QueryCount + InheritedFinancialQueryCount,
        };
    }

    static bool IsInvalidGoalSource(CommercialGoalSettingSource source) =>
        source is CommercialGoalSettingSource.InvalidDefault
            or CommercialGoalSettingSource.InvalidMonthlyOverride;

    static CommercialGoalLimitation ResolveLimitations(
        CommercialGoalSettingResolution goal,
        CommercialGoalFinancialSnapshot financial,
        CommercialGoalProgressSnapshot? progress)
    {
        var flags = CommercialGoalLimitation.ExchangesNotAdjusted;

        if (financial.CostQuality == CommercialGoalCostQuality.EstimatedLegacy)
            flags |= CommercialGoalLimitation.LegacyCostEstimate;

        if (progress?.HasLinearProjection == true)
            flags |= CommercialGoalLimitation.LinearCalendarProjection;

        if (progress is { PeriodState: CommercialGoalPeriodState.Current, HasLinearProjection: true })
            flags |= CommercialGoalLimitation.CurrentDayTreatedAsWholeDay;

        if (goal.Source == CommercialGoalSettingSource.Default)
            flags |= CommercialGoalLimitation.HistoricalDefaultCanChange;

        return flags;
    }
}
