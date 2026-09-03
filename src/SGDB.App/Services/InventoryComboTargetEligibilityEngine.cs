using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Motor puro 71A-B1: este SKU pode ser alvo de Combo Inteligente V1?
/// Consome 70C/70E/70F/70G. Sem I/O, par, preço, ranking ou UI.
/// QueryCount = 0.
/// </summary>
public static class InventoryComboTargetEligibilityEngine
{
    public const int ExpectedQueryCount = InventoryComboEligibility.ExpectedQueryCount;
    public const double Epsilon = InventoryComboEligibility.Epsilon;

    public static readonly ComboTargetEligibilityReason[] BlockerPrecedence =
    [
        ComboTargetEligibilityReason.TargetExpired,
        ComboTargetEligibilityReason.TargetExpiresToday,
        ComboTargetEligibilityReason.TargetComposition,
        ComboTargetEligibilityReason.TargetAmbiguousUnit,
        ComboTargetEligibilityReason.TargetStockUnsafe,
        ComboTargetEligibilityReason.TargetNoPhysicalEvidence,
        ComboTargetEligibilityReason.TargetReviewData,
        ComboTargetEligibilityReason.TargetZeroWithDemand,
        ComboTargetEligibilityReason.TargetAnalysisUnavailable,
    ];

    public static readonly ComboTargetEligibilityReason[] ThesisPrecedence =
    [
        ComboTargetEligibilityReason.ExpirySurplus,
        ComboTargetEligibilityReason.ProjectedExcess,
        ComboTargetEligibilityReason.Idle,
    ];

    public static InventoryComboTargetEligibility Evaluate(InventoryComboEligibilityInput? input)
    {
        input ??= new InventoryComboEligibilityInput();
        var turnover = input.Turnover;
        var attention = input.Attention;
        var facts = input.Facts;
        var guidance = input.Guidance;
        var productId = turnover?.ProductId ?? attention?.ProductId ?? facts?.ProductId ?? guidance?.ProductId ?? 0;
        var confidence = attention?.Confidence ?? InventoryAttentionConfidence.Unavailable;

        if (IsExpired(attention, guidance))
            return Blocked(productId, ComboTargetEligibilityReason.TargetExpired, confidence);
        if (IsExpiresToday(attention, guidance))
            return Blocked(productId, ComboTargetEligibilityReason.TargetExpiresToday, confidence);
        if (turnover?.IsCompositionProduct == true || facts?.IsCompositionProduct == true)
            return Blocked(productId, ComboTargetEligibilityReason.TargetComposition, confidence);
        if (InventoryComboEligibility.HasFactReason(facts, InventoryCommercialFactsReason.AmbiguousSaleUnit))
            return Blocked(productId, ComboTargetEligibilityReason.TargetAmbiguousUnit, confidence);
        if (IsNegativeStock(turnover) || turnover?.HasLocationStockAnomaly == true)
            return Blocked(productId, ComboTargetEligibilityReason.TargetStockUnsafe, confidence);
        if (turnover is not { HasPhysicalAvailabilityEvidence: true })
            return Blocked(productId, ComboTargetEligibilityReason.TargetNoPhysicalEvidence, confidence);
        if (IsReviewData(attention, guidance))
            return Blocked(productId, ComboTargetEligibilityReason.TargetReviewData, confidence);
        if (turnover?.IsZeroStockWithTurnover == true)
            return Blocked(productId, ComboTargetEligibilityReason.TargetZeroWithDemand, confidence);
        if (turnover is null || !InventoryIntelligenceEngine.IsFinite(turnover.TotalStock)
            || turnover.TotalStock <= Epsilon)
            return Blocked(productId, ComboTargetEligibilityReason.TargetStockUnsafe, confidence);
        if (attention is null || confidence == InventoryAttentionConfidence.Unavailable)
            return Blocked(productId, ComboTargetEligibilityReason.TargetAnalysisUnavailable, confidence);

        if (InventoryComboEligibility.HasPositiveQuantity(attention.ProjectedExpirySurplusQuantity))
            return Eligible(productId, ComboTargetEligibilityReason.ExpirySurplus, confidence);
        if (InventoryComboEligibility.HasPositiveQuantity(attention.ProjectedExcessQuantity))
            return Eligible(productId, ComboTargetEligibilityReason.ProjectedExcess, confidence);
        if (turnover.IsIdle)
            return Eligible(productId, ComboTargetEligibilityReason.Idle, confidence);

        return Blocked(productId, ComboTargetEligibilityReason.TargetNoTurnoverNeed, confidence);
    }

    static bool IsExpired(
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance) =>
        InventoryComboEligibility.HasAttentionReason(attention, InventoryAttentionReason.Expired)
        || guidance?.PrimaryReason == InventoryPurchaseGuidanceReason.Expired;

    static bool IsExpiresToday(
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance) =>
        InventoryComboEligibility.HasAttentionReason(attention, InventoryAttentionReason.ExpiresToday)
        || guidance?.PrimaryReason == InventoryPurchaseGuidanceReason.ExpiresToday;

    static bool IsNegativeStock(ProductTurnoverRow? turnover)
    {
        if (turnover is null)
            return false;
        return IsNegative(turnover.TotalStock)
            || IsNegative(turnover.Stock)
            || IsNegative(turnover.StockFridge);
    }

    static bool IsNegative(double value) =>
        InventoryIntelligenceEngine.IsFinite(value) && value < -Epsilon;

    static bool IsReviewData(
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance)
    {
        if (attention?.Action == InventoryOperatorAction.ReviewData)
            return true;
        if (guidance?.Action == InventoryPurchaseGuidanceAction.ReviewData)
            return true;
        if (guidance?.PrimaryReason == InventoryPurchaseGuidanceReason.StructuralDataIssue)
            return true;
        return attention?.PrimaryReason is InventoryAttentionReason.InvalidInput
            or InventoryAttentionReason.InconsistentStockTotals
            or InventoryAttentionReason.TrackedQuantityExceedsWarehouse
            or InventoryAttentionReason.DuplicateLotId
            or InventoryAttentionReason.InvalidLotQuantity
            or InventoryAttentionReason.InvalidExpiryDate
            or InventoryAttentionReason.ProjectionMissing
            or InventoryAttentionReason.DuplicateProjection;
    }

    static InventoryComboTargetEligibility Eligible(
        int productId,
        ComboTargetEligibilityReason reason,
        InventoryAttentionConfidence confidence) =>
        new()
        {
            ProductId = productId,
            Status = ComboEligibilityStatus.Eligible,
            Reason = reason,
            Confidence = confidence,
        };

    static InventoryComboTargetEligibility Blocked(
        int productId,
        ComboTargetEligibilityReason reason,
        InventoryAttentionConfidence confidence) =>
        new()
        {
            ProductId = productId,
            Status = ComboEligibilityStatus.Blocked,
            Reason = reason,
            Confidence = confidence,
        };
}
