using SGDB.Domain.Commercial;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Carga única 71B-B6+B7: B4 compositor + B5 presentation + plano qualitativo.
/// 0 SQL próprio. Inteligência 70C–71A carregada uma vez quando a competência não é futura.
/// </summary>
public static class CommercialGoalLoader
{
    public const int ExpectedQueryCount = 0;
    public const int InheritedIntelligenceQueryCount =
        CommercialGoalActionPlanSourceLoader.InheritedPipelineQueryCount;

    public static CommercialGoalPresentationSnapshot Load(
        CommercialCompetence competence,
        DateOnly referenceDate)
    {
        var goal = CommercialGoalComposerService.Load(competence, referenceDate);
        CommercialGoalActionPlanSources? sources = null;
        if (!CommercialGoalActionPlanComposer.ShouldSkipIntelligence(goal))
            sources = CommercialGoalActionPlanSourceLoader.Load();

        var plan = CommercialGoalActionPlanComposer.Compose(goal, sources);
        return CommercialGoalPresentation.Apply(
            goal,
            CommercialGoalActionPlanPresentation.Apply(plan));
    }
}
