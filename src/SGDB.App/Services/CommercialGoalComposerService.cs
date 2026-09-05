using SGDB.Domain.Commercial;

namespace SGDB.Services;

/// <summary>
/// Loader 71B-B4: resolve B3, carrega B2 e compõe B1. 0 SQL próprio.
/// </summary>
public static class CommercialGoalComposerService
{
    public const int OwnQueryCount = CommercialGoalComposer.OwnQueryCount;
    public const int InheritedFinancialQueryCount =
        CommercialGoalComposer.InheritedFinancialQueryCount;

    public static CommercialGoalSnapshot Load(
        CommercialCompetence competence,
        DateOnly referenceDate)
    {
        var goal = CommercialGoalSettingsService.Resolve(competence);
        var financial = CommercialGoalFinancialService.Load(competence);
        return CommercialGoalComposer.Compose(goal, financial, referenceDate);
    }

    public static CommercialGoalSnapshot Load(
        CommercialCompetence competence,
        DateTime referenceDate) =>
        Load(competence, DateOnly.FromDateTime(referenceDate));
}
