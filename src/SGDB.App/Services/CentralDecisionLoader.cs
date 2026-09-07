using SGDB.Domain.Commercial;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Loader único 71C-B4. Orquestra Meta + fontes 70C–71A + B7 + B8 + B2 + B3.
/// 0 SQL próprio. 0 I/O próprio. Sem WPF, cache, timer ou ranking.
/// 70F/71A seguem o SourceLoader atômico; sem try/catch parcial.
/// </summary>
public static class CentralDecisionLoader
{
    public const int ExpectedQueryCount = 0;
    public const int MaxInheritedQueryCount = 15;
    public const int InheritedSettingsMaxQueryCount =
        CommercialGoalSettingsService.ExpectedResolveMaxQueryCount;
    public const int InheritedFinancialQueryCount =
        CommercialGoalComposerService.InheritedFinancialQueryCount;
    public const int InheritedIntelligenceQueryCount =
        CommercialGoalActionPlanSourceLoader.InheritedPipelineQueryCount;
    public const int InheritedProductContributionQueryCount =
        CommercialGoalProductContributionService.ExpectedQueryCount;

    public static CentralDecisionLoadResult Load(
        CommercialCompetence competence,
        DateOnly referenceDate)
    {
        var goal = CommercialGoalComposerService.Load(competence, referenceDate);
        CommercialGoalActionPlanSources? sources = null;
        if (!CommercialGoalActionPlanComposer.ShouldSkipIntelligence(goal))
        {
            sources = CommercialGoalActionPlanSourceLoader.Load(
                referenceDate.ToDateTime(TimeOnly.MinValue));
        }

        var contribution = CommercialGoalProductContributionService.Load(competence);
        return Assemble(goal, sources, contribution);
    }

    public static CentralDecisionLoadResult Load(
        CommercialCompetence competence,
        DateTime referenceDate) =>
        Load(competence, DateOnly.FromDateTime(referenceDate));

    /// <summary>
    /// Compõe autoridades já carregadas. Sem I/O. Usado pelo Load e por testes de pipeline.
    /// </summary>
    public static CentralDecisionLoadResult Assemble(
        CommercialGoalSnapshot goal,
        CommercialGoalActionPlanSources? sources,
        CommercialGoalProductContributionSnapshot? contribution)
    {
        ArgumentNullException.ThrowIfNull(goal);
        var plan = CommercialGoalActionPlanComposer.Compose(goal, sources);
        var decision = CentralDecisionComposer.Compose(goal, plan, contribution, sources);
        var presentation = CentralDecisionPresentation.Apply(decision);
        InventoryProjectionPresentationSnapshot? projectionPresented = null;
        InventoryAttentionPresentationSnapshot? attentionPresented = null;
        InventoryPromotionSuggestionPresentationSnapshot? promotionPresented = null;
        InventoryPurchaseGuidancePresentationSnapshot? guidancePresented = null;
        var projection = sources?.Projection;
        if (projection is not null)
        {
            projectionPresented = InventoryProjectionPresentation.Apply(projection);
            if (sources?.Attention is not null)
                attentionPresented = InventoryAttentionPresentation.Apply(sources.Attention, projectionPresented);
            if (sources?.Promotion is not null)
                promotionPresented = InventoryPromotionSuggestionPresentation.Apply(sources.Promotion);
            if (sources?.Guidance is not null)
            {
                guidancePresented = InventoryPurchaseGuidancePresentation.Apply(
                    sources.Guidance, projection.Intelligence, projection);
            }
        }

        return new CentralDecisionLoadResult
        {
            Decision = decision,
            Presentation = presentation,
            Sources = sources,
            Projection = projection,
            ProjectionPresented = projectionPresented,
            AttentionPresented = attentionPresented,
            PromotionPresented = promotionPresented,
            GuidancePresented = guidancePresented,
        };
    }
}
