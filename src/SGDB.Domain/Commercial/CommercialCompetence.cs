namespace SGDB.Domain.Commercial;

/// <summary>
/// Competência mensal civil YYYY-MM. Sem calendário comercial, feriado ou horário.
/// </summary>
public readonly record struct CommercialCompetence
{
    public int Year { get; }
    public int Month { get; }
    public int DaysInMonth { get; }
    public DateOnly StartDate { get; }
    public DateOnly EndDate { get; }

    CommercialCompetence(int year, int month, int daysInMonth, DateOnly start, DateOnly end)
    {
        Year = year;
        Month = month;
        DaysInMonth = daysInMonth;
        StartDate = start;
        EndDate = end;
    }

    public static CommercialCompetence Create(int year, int month)
    {
        if (year < 1 || year > 9999)
            throw new ArgumentOutOfRangeException(nameof(year), year, "Ano da competência inválido.");
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), month, "Mês da competência inválido.");

        var days = DateTime.DaysInMonth(year, month);
        var start = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month, days);
        return new CommercialCompetence(year, month, days, start, end);
    }

    public static CommercialCompetence FromDate(DateOnly date) =>
        Create(date.Year, date.Month);

    public CommercialGoalPeriodState Classify(DateOnly referenceDate)
    {
        if (referenceDate < StartDate)
            return CommercialGoalPeriodState.Future;
        if (referenceDate > EndDate)
            return CommercialGoalPeriodState.Closed;
        return CommercialGoalPeriodState.Current;
    }

    public override string ToString() =>
        $"{Year:0000}-{Month:00}";
}
