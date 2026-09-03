using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Motor puro 71A-B1: este SKU pode ser âncora de Combo Inteligente V1?
/// Consome 70C/70E/70F/70G. Sem I/O, par, preço, ranking ou UI.
/// QueryCount = 0.
/// Giro observável é obrigatório: VMV30 ≤ ε bloqueia (conflito A2 §10 vs §11 — prevalece segurança).
/// </summary>
public static class InventoryComboAnchorEligibilityEngine
{
    public const int ExpectedQueryCount = InventoryComboEligibility.ExpectedQueryCount;
    public const double Epsilon = InventoryComboEligibility.Epsilon;

    public static InventoryComboAnchorEligibility Evaluate(InventoryComboEligibilityInput? input)
    {
        input ??= new InventoryComboEligibilityInput();
        var turnover = input.Turnover;
        var attention = input.Attention;
        var facts = input.Facts;
        var guidance = input.Guidance;
        var productId = turnover?.ProductId ?? attention?.ProductId ?? facts?.ProductId ?? guidance?.ProductId ?? 0;
        var confidence = attention?.Confidence ?? InventoryAttentionConfidence.Unavailable;

        if (turnover?.IsCompositionProduct == true || facts?.IsCompositionProduct == true)
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorComposition, confidence);
        if (InventoryComboEligibility.HasFactReason(facts, InventoryCommercialFactsReason.AmbiguousSaleUnit))
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorAmbiguousUnit, confidence);
        if (turnover is null
            || !InventoryIntelligenceEngine.IsFinite(turnover.TotalStock)
            || turnover.TotalStock <= Epsilon
            || IsNegative(turnover.TotalStock)
            || IsNegative(turnover.Stock)
            || IsNegative(turnover.StockFridge))
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorStockUnsafe, confidence);
        if (turnover.HasLocationStockAnomaly
            || guidance?.PrimaryReason == InventoryPurchaseGuidanceReason.LocationLimitation)
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorLocationAnomaly, confidence);
        if (!turnover.HasPhysicalAvailabilityEvidence)
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorNoPhysicalEvidence, confidence);
        if (turnover.IsHistoryInsufficient30
            || turnover.HistoryDays < InventoryProjectionEngine.MinHistoryDays
            || guidance?.PrimaryReason == InventoryPurchaseGuidanceReason.InsufficientHistory)
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorInsufficientHistory, confidence);
        if (facts is null
            || facts.CostQuality != InventoryCommercialCostQuality.Known
            || facts.PriceQuality != InventoryCommercialPriceQuality.Usable)
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorFinancialUnavailable, confidence);
        if (!facts.CanEvaluateFinancialScenario)
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorFinancialUnavailable, confidence);
        if (IsExpiryUrgent(attention, guidance))
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorExpiryUrgent, confidence);
        if (IsReviewData(attention, guidance))
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorReviewData, confidence);
        if (IsNotApplicable(guidance))
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorNotApplicable, confidence);
        if (IsConsiderReplenishment(guidance))
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorConsiderReplenishment, confidence);
        if (IsDoNotReplenishNowBlocked(guidance))
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorDoNotReplenishNow, confidence);
        if (!HasObservableDemand(turnover)
            || guidance?.PrimaryReason == InventoryPurchaseGuidanceReason.NoObservableDemand)
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorNoObservableDemand, confidence);
        if (turnover.CoverageBand != InventoryCoverageBand.Normal)
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorCoverageUnsafe, confidence);
        if (!PassesUnitGuardrail(turnover))
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorUnitGuardrail, confidence);
        if (attention is null || confidence == InventoryAttentionConfidence.Unavailable)
            return Blocked(productId, ComboAnchorEligibilityReason.AnchorReviewData, confidence);

        return new InventoryComboAnchorEligibility
        {
            ProductId = productId,
            Status = ComboEligibilityStatus.Eligible,
            Reason = ComboAnchorEligibilityReason.HealthyNormalCoverage,
            Confidence = confidence,
        };
    }

    static bool IsNegative(double value) =>
        InventoryIntelligenceEngine.IsFinite(value) && value < -Epsilon;

    static bool IsExpiryUrgent(
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance)
    {
        if (attention?.Family == InventoryAttentionFamily.Expiry)
            return true;
        if (InventoryComboEligibility.HasAttentionReason(attention, InventoryAttentionReason.Expired)
            || InventoryComboEligibility.HasAttentionReason(attention, InventoryAttentionReason.ExpiresToday)
            || InventoryComboEligibility.HasAttentionReason(attention, InventoryAttentionReason.SurplusAtExpiry)
            || InventoryComboEligibility.HasAttentionReason(attention, InventoryAttentionReason.NearExpiryWithoutSurplus))
            return true;
        return guidance?.PrimaryReason is InventoryPurchaseGuidanceReason.Expired
            or InventoryPurchaseGuidanceReason.ExpiresToday;
    }

    static bool IsReviewData(
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance) =>
        attention?.Action == InventoryOperatorAction.ReviewData
        || guidance is null
        || guidance.Action == InventoryPurchaseGuidanceAction.ReviewData
        || guidance.PrimaryReason == InventoryPurchaseGuidanceReason.StructuralDataIssue;

    static bool IsNotApplicable(InventoryPurchaseGuidanceResult? guidance) =>
        guidance is not null
        && (guidance.Action == InventoryPurchaseGuidanceAction.None
            || guidance.Status == InventoryPurchaseGuidanceStatus.NotApplicable);

    static bool IsConsiderReplenishment(InventoryPurchaseGuidanceResult? guidance) =>
        guidance is not null
        && (guidance.Action == InventoryPurchaseGuidanceAction.ConsiderReplenishment
            || guidance.PrimaryReason is InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand
                or InventoryPurchaseGuidanceReason.CriticalCoverage
                or InventoryPurchaseGuidanceReason.LowCoverage);

    static bool IsDoNotReplenishNowBlocked(InventoryPurchaseGuidanceResult? guidance)
    {
        if (guidance is null)
            return false;
        if (guidance.PrimaryReason is InventoryPurchaseGuidanceReason.ProjectedExcess30
            or InventoryPurchaseGuidanceReason.ProjectedExpirySurplus
            or InventoryPurchaseGuidanceReason.IdleStock
            or InventoryPurchaseGuidanceReason.Expired
            or InventoryPurchaseGuidanceReason.ExpiresToday)
            return true;
        return guidance.Action == InventoryPurchaseGuidanceAction.DoNotReplenishNow;
    }

    static bool HasObservableDemand(ProductTurnoverRow turnover) =>
        InventoryIntelligenceEngine.IsFinite(turnover.Vmv30)
        && turnover.Vmv30 > Epsilon;

    /// <summary>
    /// Após vender 1 unidade, a cobertura restante deve ser &gt; 15 dias
    /// (<see cref="InventoryIntelligenceEngine.LowCoverageDaysThreshold"/>).
    /// VMV30 ≤ ε não chega aqui: já bloqueado por demanda não observável.
    /// </summary>
    static bool PassesUnitGuardrail(ProductTurnoverRow turnover)
    {
        var vmv = turnover.Vmv30;
        if (!InventoryIntelligenceEngine.IsFinite(vmv) || vmv <= Epsilon)
            return turnover.TotalStock >= 2 - Epsilon;

        var remaining = (turnover.TotalStock - 1) / vmv;
        return InventoryIntelligenceEngine.IsFinite(remaining)
            && remaining > InventoryIntelligenceEngine.LowCoverageDaysThreshold;
    }

    static InventoryComboAnchorEligibility Blocked(
        int productId,
        ComboAnchorEligibilityReason reason,
        InventoryAttentionConfidence confidence) =>
        new()
        {
            ProductId = productId,
            Status = ComboEligibilityStatus.Blocked,
            Reason = reason,
            Confidence = confidence,
        };
}
