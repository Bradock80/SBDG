using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Carga única das autoridades 70C–71A para o plano B7.
/// 1 load de cada fonte. Sem SQL próprio. Sem N+1.
/// </summary>
public static class CommercialGoalActionPlanSourceLoader
{
    public const int ExpectedQueryCount = 0;
    public const int ExpectedProjectionLoads = 1;
    public const int ExpectedAttentionBuilds = 1;
    public const int ExpectedFactsLoads = 1;
    public const int ExpectedMarginLoads = 1;
    public const int ExpectedGuidanceComposes = 1;
    public const int ExpectedPromotionComposes = 1;
    public const int ExpectedComboComposes = 1;

    public const int InheritedPipelineQueryCount =
        InventoryComboIntelligenceComposer.ExpectedPipelineQueryCount;

    public static CommercialGoalActionPlanSources Load(DateTime? today = null)
    {
        var projection = InventoryProjectionService.Load(today);
        var attention = InventoryAttentionComposer.Build(projection);
        var eligibility = InventoryCommercialEligibilityComposer.Build(projection, attention);
        var facts = InventoryCommercialFactsService.Load(
            InventoryCommercialEligibilityComposer.ProductIds(projection));
        var setting = InventoryCommercialMarginSettingsService.Load();
        var policy = InventoryCommercialMarginPolicyResolver.Resolve(setting);
        var guidance = InventoryPurchaseGuidanceComposer.Compose(projection);
        var scenarios = InventoryCommercialScenarioComposer.Compose(
            projection.Intelligence,
            projection,
            attention,
            eligibility,
            facts,
            policy);
        var promotion = InventoryPromotionSuggestionComposer.Compose(
            projection.Intelligence,
            scenarios);
        var combos = InventoryComboIntelligenceComposer.Compose(new InventoryComboIntelligenceComposeInput
        {
            Today = projection.Today,
            Intelligence = projection.Intelligence,
            Attention = attention,
            Facts = facts,
            Guidance = guidance,
            PolicyResolution = policy,
        });

        return new CommercialGoalActionPlanSources
        {
            Intelligence = projection.Intelligence,
            Attention = attention,
            Promotion = promotion,
            Guidance = guidance,
            Combos = combos,
            QueryCount = combos.QueryCount,
        };
    }
}
