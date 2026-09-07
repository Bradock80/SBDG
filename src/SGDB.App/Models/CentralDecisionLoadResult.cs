namespace SGDB.Models;

/// <summary>
/// Resultado 71C-B4. Referências aos snapshots existentes; sem duplicar payload.
/// </summary>
public sealed class CentralDecisionLoadResult
{
    public required CentralDecisionSnapshot Decision { get; init; }
    public required CentralDecisionPresentationSnapshot Presentation { get; init; }
    public CommercialGoalActionPlanSources? Sources { get; init; }
    public InventoryProjectionSnapshot? Projection { get; init; }
    public InventoryProjectionPresentationSnapshot? ProjectionPresented { get; init; }
    public InventoryAttentionPresentationSnapshot? AttentionPresented { get; init; }
    public InventoryPromotionSuggestionPresentationSnapshot? PromotionPresented { get; init; }
    public InventoryPurchaseGuidancePresentationSnapshot? GuidancePresented { get; init; }

    public int QueryCount => Decision.QueryCount;
}
