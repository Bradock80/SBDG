using SGDB.Domain.Commercial;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Carga única 71B-B6+B7+B8: B4 compositor + B5 presentation + plano qualitativo + contribuição.
/// 0 SQL próprio. B8B = 1 query herdada. Inteligência 70C–71A uma vez se a competência não é futura.
/// </summary>
public static class CommercialGoalLoader
{
    public const int ExpectedQueryCount = 0;
    public const int InheritedIntelligenceQueryCount =
        CommercialGoalActionPlanSourceLoader.InheritedPipelineQueryCount;
    public const int InheritedProductContributionQueryCount =
        CommercialGoalProductContributionService.ExpectedQueryCount;

    public static CommercialGoalPresentationSnapshot Load(
        CommercialCompetence competence,
        DateOnly referenceDate)
    {
        var goal = CommercialGoalComposerService.Load(competence, referenceDate);
        CommercialGoalActionPlanSources? sources = null;
        if (!CommercialGoalActionPlanComposer.ShouldSkipIntelligence(goal))
            sources = CommercialGoalActionPlanSourceLoader.Load();

        var plan = CommercialGoalActionPlanComposer.Compose(goal, sources);
        var contribution = CommercialGoalProductContributionPresentation.Apply(
            CommercialGoalProductContributionService.Load(competence));
        return CommercialGoalPresentation.Apply(
            goal,
            CommercialGoalActionPlanPresentation.Apply(plan),
            contribution);
    }
}
