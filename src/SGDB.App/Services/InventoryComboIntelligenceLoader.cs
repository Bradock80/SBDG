using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Carga única 71A-B7: pipeline B5 + presentation B6.
/// Sem regra nova. QueryCount = 9 sem B2, 10 com B2. B7 = +0.
/// </summary>
public static class InventoryComboIntelligenceLoader
{
    public const int ExpectedQueryCount = 0;
    public const int ExpectedPipelineQueryCount =
        InventoryComboIntelligenceComposer.ExpectedPipelineQueryCount;

    public static InventoryComboPresentationSnapshot Load(DateTime? today = null) =>
        InventoryComboPresentation.Apply(LoadSnapshot(today));

    public static InventoryComboIntelligenceSnapshot LoadSnapshot(DateTime? today = null)
    {
        var projection = InventoryProjectionService.Load(today);
        var attention = InventoryAttentionComposer.Build(projection);
        var facts = InventoryCommercialFactsService.Load(
            InventoryCommercialEligibilityComposer.ProductIds(projection));
        var setting = InventoryCommercialMarginSettingsService.Load();
        var policy = InventoryCommercialMarginPolicyResolver.Resolve(setting);
        var guidance = InventoryPurchaseGuidanceComposer.Compose(projection);
        return InventoryComboIntelligenceComposer.Compose(new InventoryComboIntelligenceComposeInput
        {
            Today = projection.Today,
            Intelligence = projection.Intelligence,
            Attention = attention,
            Facts = facts,
            Guidance = guidance,
            PolicyResolution = policy,
        });
    }
}
