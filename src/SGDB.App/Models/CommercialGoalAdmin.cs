using SGDB.Domain.Commercial;

namespace SGDB.Models;

public sealed class CommercialGoalAdminSnapshot
{
    public CommercialCompetence Competence { get; init; }
    public string CompetenceTitle { get; init; } = "";
    public string OriginText { get; init; } = "";
    public string DefaultEditorText { get; init; } = "";
    public string MonthlyEditorText { get; init; } = "";
    public string DefaultStatusText { get; init; } = "";
    public string MonthlyStatusText { get; init; } = "";
    public string HistoricalDefaultNote { get; init; } = "";
    public bool HasDefault { get; init; }
    public bool HasMonthlyOverride { get; init; }
    public bool CanMutate { get; init; }
    public bool StationAllowsWrite { get; init; }
}

public sealed class CommercialGoalAdminResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = "";
    public CommercialGoalAdminSnapshot Snapshot { get; init; } = new();
}
