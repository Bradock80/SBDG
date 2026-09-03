using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Motor puro 70F-B5B: interpreta o resultado B4 como orientação comercial.
/// Sem I/O, UI, SQL, PDV, promoção ativa, combo, meta ou recálculo financeiro.
/// Suggested ⇔ B4 Available com tese ExpirySurplus ou ProjectedExcess30.
/// </summary>
public static class InventoryPromotionSuggestionEngine
{
    public const int ExpectedQueryCount = 0;

    public static readonly InventoryPromotionSuggestionReason[] ReasonPrecedence =
    [
        InventoryPromotionSuggestionReason.InvalidInput,
        InventoryPromotionSuggestionReason.ScenarioMissing,
        InventoryPromotionSuggestionReason.DuplicateScenario,
        InventoryPromotionSuggestionReason.Expired,
        InventoryPromotionSuggestionReason.LocationLimitation,
        InventoryPromotionSuggestionReason.ReviewData,
        InventoryPromotionSuggestionReason.LimitedConfidence,
        InventoryPromotionSuggestionReason.UnavailableConfidence,
        InventoryPromotionSuggestionReason.ExpiresToday,
        InventoryPromotionSuggestionReason.NearExpiryWithoutSurplus,
        InventoryPromotionSuggestionReason.DatedWithoutSurplusInWindow,
        InventoryPromotionSuggestionReason.IdleOnly,
        InventoryPromotionSuggestionReason.HighCoverageOnly,
        InventoryPromotionSuggestionReason.PolicyMissing,
        InventoryPromotionSuggestionReason.PolicyInvalid,
        InventoryPromotionSuggestionReason.MissingProduct,
        InventoryPromotionSuggestionReason.UnknownCost,
        InventoryPromotionSuggestionReason.InvalidCost,
        InventoryPromotionSuggestionReason.NotSellable,
        InventoryPromotionSuggestionReason.CompositionProduct,
        InventoryPromotionSuggestionReason.AmbiguousSaleUnit,
        InventoryPromotionSuggestionReason.FinancialDataUnavailable,
        InventoryPromotionSuggestionReason.NotApplicable,
        InventoryPromotionSuggestionReason.SuggestedBecauseExpirySurplus,
        InventoryPromotionSuggestionReason.SuggestedBecauseProjectedExcess,
    ];

    public static InventoryPromotionSuggestionResult Evaluate(
        InventoryCommercialScenarioResult? scenario,
        InventoryAttentionPriority? attentionPriority = null,
        bool hasWholesalePricing = false) =>
        Evaluate(new InventoryPromotionSuggestionInput
        {
            Scenario = scenario,
            AttentionPriority = attentionPriority,
            HasWholesalePricing = hasWholesalePricing,
        });

    public static InventoryPromotionSuggestionResult Evaluate(
        InventoryPromotionSuggestionInput? input)
    {
        input ??= new InventoryPromotionSuggestionInput();
        var scenario = input.Scenario;
        if (scenario is null)
        {
            return new InventoryPromotionSuggestionResult
            {
                PrimaryReason = InventoryPromotionSuggestionReason.InvalidInput,
                SecondaryReasons = [],
                Warnings = [],
                Scenarios = [],
            };
        }

        var collected = Collect(scenario);
        var status = ResolveStatus(scenario, collected);
        var suggested = status == InventoryPromotionSuggestionStatus.Suggested;
        if (suggested)
        {
            Add(collected, scenario.Thesis == InventoryCommercialScenarioThesis.ExpirySurplus
                ? InventoryPromotionSuggestionReason.SuggestedBecauseExpirySurplus
                : InventoryPromotionSuggestionReason.SuggestedBecauseProjectedExcess);
        }

        var primary = suggested
            ? SuggestedReasonOf(scenario.Thesis)
            : SelectPrimary(collected);
        var secondary = SelectSecondary(collected, primary);
        var action = ResolveAction(status, collected, scenario.Thesis);
        var objective = ResolveObjective(status, action, scenario.Thesis, collected);

        return new InventoryPromotionSuggestionResult
        {
            ProductId = scenario.ProductId,
            Status = status,
            Action = action,
            Thesis = scenario.Thesis,
            Objective = objective,
            Confidence = scenario.Confidence,
            AttentionPriority = input.AttentionPriority,
            PrimaryReason = primary,
            SecondaryReasons = secondary,
            Warnings = BuildWarnings(suggested, scenario, input.HasWholesalePricing),
            AttentionQuantity = scenario.AttentionQuantity,
            AttentionQuantitySource = scenario.AttentionQuantitySource,
            Scenarios = suggested ? PreserveScenarios(scenario.Scenarios) : [],
        };
    }

    static InventoryPromotionSuggestionStatus ResolveStatus(
        InventoryCommercialScenarioResult scenario,
        List<InventoryPromotionSuggestionReason> collected)
    {
        if (scenario.Status == InventoryCommercialScenarioStatus.Expired
            || Has(collected, InventoryPromotionSuggestionReason.Expired))
            return InventoryPromotionSuggestionStatus.Expired;

        if (scenario.Confidence == InventoryAttentionConfidence.Limited
            || Has(collected, InventoryPromotionSuggestionReason.LimitedConfidence))
            return MapNonSuggested(scenario.Status, InventoryPromotionSuggestionStatus.MonitorOnly);

        if (scenario.Confidence == InventoryAttentionConfidence.Unavailable
            || Has(collected, InventoryPromotionSuggestionReason.UnavailableConfidence))
            return MapUnavailable(scenario.Status);

        if (scenario.Status == InventoryCommercialScenarioStatus.ReviewData
            || Has(collected, InventoryPromotionSuggestionReason.LocationLimitation)
            || Has(collected, InventoryPromotionSuggestionReason.ReviewData))
            return InventoryPromotionSuggestionStatus.ReviewData;

        if (BlocksNumericSuggestion(collected))
            return MapBlockedFinancialOrReview(collected, scenario.Status);

        if (scenario.Status == InventoryCommercialScenarioStatus.Available
            && scenario.Thesis is InventoryCommercialScenarioThesis.ExpirySurplus
                or InventoryCommercialScenarioThesis.ProjectedExcess30)
            return InventoryPromotionSuggestionStatus.Suggested;

        return MapStatus(scenario.Status);
    }

    static InventoryPromotionSuggestionStatus MapNonSuggested(
        InventoryCommercialScenarioStatus b4,
        InventoryPromotionSuggestionStatus fallback)
    {
        if (b4 == InventoryCommercialScenarioStatus.Expired)
            return InventoryPromotionSuggestionStatus.Expired;
        if (b4 == InventoryCommercialScenarioStatus.ReviewData)
            return InventoryPromotionSuggestionStatus.ReviewData;
        if (b4 == InventoryCommercialScenarioStatus.Available)
            return fallback;
        return MapStatus(b4);
    }

    static InventoryPromotionSuggestionStatus MapUnavailable(InventoryCommercialScenarioStatus b4) =>
        b4 switch
        {
            InventoryCommercialScenarioStatus.Expired => InventoryPromotionSuggestionStatus.Expired,
            InventoryCommercialScenarioStatus.ReviewData => InventoryPromotionSuggestionStatus.ReviewData,
            InventoryCommercialScenarioStatus.NoRecommendation =>
                InventoryPromotionSuggestionStatus.NotApplicable,
            InventoryCommercialScenarioStatus.FinancialDataUnavailable =>
                InventoryPromotionSuggestionStatus.FinancialDataUnavailable,
            InventoryCommercialScenarioStatus.Available =>
                InventoryPromotionSuggestionStatus.FinancialDataUnavailable,
            _ => InventoryPromotionSuggestionStatus.FinancialDataUnavailable,
        };

    static InventoryPromotionSuggestionStatus MapBlockedFinancialOrReview(
        List<InventoryPromotionSuggestionReason> collected,
        InventoryCommercialScenarioStatus b4)
    {
        if (Has(collected, InventoryPromotionSuggestionReason.LocationLimitation)
            || Has(collected, InventoryPromotionSuggestionReason.ReviewData))
            return InventoryPromotionSuggestionStatus.ReviewData;
        if (b4 == InventoryCommercialScenarioStatus.Available)
            return InventoryPromotionSuggestionStatus.FinancialDataUnavailable;
        return MapStatus(b4);
    }

    static bool BlocksNumericSuggestion(List<InventoryPromotionSuggestionReason> collected) =>
        Has(collected, InventoryPromotionSuggestionReason.CompositionProduct)
        || Has(collected, InventoryPromotionSuggestionReason.AmbiguousSaleUnit)
        || Has(collected, InventoryPromotionSuggestionReason.UnknownCost)
        || Has(collected, InventoryPromotionSuggestionReason.InvalidCost)
        || Has(collected, InventoryPromotionSuggestionReason.MissingProduct)
        || Has(collected, InventoryPromotionSuggestionReason.NotSellable)
        || Has(collected, InventoryPromotionSuggestionReason.LocationLimitation);

    static InventoryPromotionSuggestionStatus MapStatus(InventoryCommercialScenarioStatus status) =>
        status switch
        {
            InventoryCommercialScenarioStatus.Available => InventoryPromotionSuggestionStatus.MonitorOnly,
            InventoryCommercialScenarioStatus.MonitorOnly => InventoryPromotionSuggestionStatus.MonitorOnly,
            InventoryCommercialScenarioStatus.ReviewData => InventoryPromotionSuggestionStatus.ReviewData,
            InventoryCommercialScenarioStatus.NoRecommendation =>
                InventoryPromotionSuggestionStatus.NotApplicable,
            InventoryCommercialScenarioStatus.PolicyMissing =>
                InventoryPromotionSuggestionStatus.PolicyMissing,
            InventoryCommercialScenarioStatus.PolicyInvalid =>
                InventoryPromotionSuggestionStatus.PolicyInvalid,
            InventoryCommercialScenarioStatus.FinancialDataUnavailable =>
                InventoryPromotionSuggestionStatus.FinancialDataUnavailable,
            InventoryCommercialScenarioStatus.Expired => InventoryPromotionSuggestionStatus.Expired,
            _ => InventoryPromotionSuggestionStatus.NotApplicable,
        };

    static InventoryPromotionSuggestionAction ResolveAction(
        InventoryPromotionSuggestionStatus status,
        List<InventoryPromotionSuggestionReason> collected,
        InventoryCommercialScenarioThesis thesis)
    {
        if (status == InventoryPromotionSuggestionStatus.Expired)
            return InventoryPromotionSuggestionAction.RemoveExpired;
        if (status == InventoryPromotionSuggestionStatus.Suggested)
            return InventoryPromotionSuggestionAction.ConsiderPromotion;
        if (status is InventoryPromotionSuggestionStatus.ReviewData
            or InventoryPromotionSuggestionStatus.PolicyMissing
            or InventoryPromotionSuggestionStatus.PolicyInvalid
            or InventoryPromotionSuggestionStatus.FinancialDataUnavailable)
            return InventoryPromotionSuggestionAction.ReviewData;
        if (status == InventoryPromotionSuggestionStatus.NotApplicable)
            return InventoryPromotionSuggestionAction.None;
        if (Has(collected, InventoryPromotionSuggestionReason.ExpiresToday)
            || Has(collected, InventoryPromotionSuggestionReason.NearExpiryWithoutSurplus)
            || Has(collected, InventoryPromotionSuggestionReason.DatedWithoutSurplusInWindow)
            || thesis == InventoryCommercialScenarioThesis.Idle
            || Has(collected, InventoryPromotionSuggestionReason.IdleOnly))
            return InventoryPromotionSuggestionAction.PrioritizeExposure;
        return InventoryPromotionSuggestionAction.Monitor;
    }

    static InventoryPromotionSuggestionObjective ResolveObjective(
        InventoryPromotionSuggestionStatus status,
        InventoryPromotionSuggestionAction action,
        InventoryCommercialScenarioThesis thesis,
        List<InventoryPromotionSuggestionReason> collected)
    {
        if (status == InventoryPromotionSuggestionStatus.Suggested
            && thesis == InventoryCommercialScenarioThesis.ExpirySurplus)
            return InventoryPromotionSuggestionObjective.ReduceProjectedExpirySurplus;
        if (status == InventoryPromotionSuggestionStatus.Suggested)
            return InventoryPromotionSuggestionObjective.ReduceProjectedExcess30;
        if (status == InventoryPromotionSuggestionStatus.Expired)
            return InventoryPromotionSuggestionObjective.RemoveExpired;
        if (status is InventoryPromotionSuggestionStatus.ReviewData
            or InventoryPromotionSuggestionStatus.PolicyMissing
            or InventoryPromotionSuggestionStatus.PolicyInvalid
            or InventoryPromotionSuggestionStatus.FinancialDataUnavailable
            || Has(collected, InventoryPromotionSuggestionReason.LimitedConfidence))
            return InventoryPromotionSuggestionObjective.ReviewInformation;
        if (action == InventoryPromotionSuggestionAction.PrioritizeExposure)
            return InventoryPromotionSuggestionObjective.IncreaseCommercialAttention;
        if (status == InventoryPromotionSuggestionStatus.NotApplicable)
            return InventoryPromotionSuggestionObjective.None;
        return InventoryPromotionSuggestionObjective.MonitorTurnover;
    }

    static InventoryPromotionSuggestionReason SuggestedReasonOf(
        InventoryCommercialScenarioThesis thesis) =>
        thesis == InventoryCommercialScenarioThesis.ExpirySurplus
            ? InventoryPromotionSuggestionReason.SuggestedBecauseExpirySurplus
            : InventoryPromotionSuggestionReason.SuggestedBecauseProjectedExcess;

    static List<InventoryPromotionSuggestionReason> Collect(
        InventoryCommercialScenarioResult scenario)
    {
        var collected = new List<InventoryPromotionSuggestionReason>(8);
        if (Map(scenario.PrimaryReason) is { } primary)
            Add(collected, primary);
        foreach (var reason in scenario.SecondaryReasons ?? [])
        {
            if (Map(reason) is { } mapped)
                Add(collected, mapped);
        }

        if (scenario.Confidence == InventoryAttentionConfidence.Limited)
            Add(collected, InventoryPromotionSuggestionReason.LimitedConfidence);
        if (scenario.Confidence == InventoryAttentionConfidence.Unavailable)
            Add(collected, InventoryPromotionSuggestionReason.UnavailableConfidence);
        if (scenario.Thesis == InventoryCommercialScenarioThesis.Idle)
            Add(collected, InventoryPromotionSuggestionReason.IdleOnly);
        if (scenario.Thesis == InventoryCommercialScenarioThesis.HighCoverage)
            Add(collected, InventoryPromotionSuggestionReason.HighCoverageOnly);
        return collected;
    }

    static InventoryPromotionSuggestionReason? Map(InventoryCommercialScenarioReason reason) =>
        reason switch
        {
            InventoryCommercialScenarioReason.Expired => InventoryPromotionSuggestionReason.Expired,
            InventoryCommercialScenarioReason.LocationLimitation =>
                InventoryPromotionSuggestionReason.LocationLimitation,
            InventoryCommercialScenarioReason.ExpiresToday =>
                InventoryPromotionSuggestionReason.ExpiresToday,
            InventoryCommercialScenarioReason.NearExpiryWithoutSurplus =>
                InventoryPromotionSuggestionReason.NearExpiryWithoutSurplus,
            InventoryCommercialScenarioReason.DatedWithoutSurplusInWindow =>
                InventoryPromotionSuggestionReason.DatedWithoutSurplusInWindow,
            InventoryCommercialScenarioReason.Idle => InventoryPromotionSuggestionReason.IdleOnly,
            InventoryCommercialScenarioReason.HighCoverageMonitoring =>
                InventoryPromotionSuggestionReason.HighCoverageOnly,
            InventoryCommercialScenarioReason.LimitedConfidence =>
                InventoryPromotionSuggestionReason.LimitedConfidence,
            InventoryCommercialScenarioReason.UnavailableConfidence =>
                InventoryPromotionSuggestionReason.UnavailableConfidence,
            InventoryCommercialScenarioReason.PolicyMissing =>
                InventoryPromotionSuggestionReason.PolicyMissing,
            InventoryCommercialScenarioReason.PolicyInvalid =>
                InventoryPromotionSuggestionReason.PolicyInvalid,
            InventoryCommercialScenarioReason.MissingProduct =>
                InventoryPromotionSuggestionReason.MissingProduct,
            InventoryCommercialScenarioReason.UnknownCost =>
                InventoryPromotionSuggestionReason.UnknownCost,
            InventoryCommercialScenarioReason.InvalidCost =>
                InventoryPromotionSuggestionReason.InvalidCost,
            InventoryCommercialScenarioReason.NotSellable =>
                InventoryPromotionSuggestionReason.NotSellable,
            InventoryCommercialScenarioReason.CompositionProduct =>
                InventoryPromotionSuggestionReason.CompositionProduct,
            InventoryCommercialScenarioReason.AmbiguousSaleUnit =>
                InventoryPromotionSuggestionReason.AmbiguousSaleUnit,
            InventoryCommercialScenarioReason.NoRecommendation =>
                InventoryPromotionSuggestionReason.NotApplicable,
            InventoryCommercialScenarioReason.InvalidInput
                or InventoryCommercialScenarioReason.NegativeStock
                or InventoryCommercialScenarioReason.NegativeLocationStock
                or InventoryCommercialScenarioReason.NegativeWarehouseStock
                or InventoryCommercialScenarioReason.InconsistentStockTotals
                or InventoryCommercialScenarioReason.TrackedQuantityExceedsWarehouse
                or InventoryCommercialScenarioReason.DuplicateLotId
                or InventoryCommercialScenarioReason.InvalidLotQuantity
                or InventoryCommercialScenarioReason.InvalidExpiryDate
                or InventoryCommercialScenarioReason.ProjectionMissing
                or InventoryCommercialScenarioReason.DuplicateProjection
                or InventoryCommercialScenarioReason.InsufficientHistory
                or InventoryCommercialScenarioReason.NoPhysicalEvidence
                or InventoryCommercialScenarioReason.Undated
                or InventoryCommercialScenarioReason.NoLot
                or InventoryCommercialScenarioReason.ReviewData =>
                InventoryPromotionSuggestionReason.ReviewData,
            InventoryCommercialScenarioReason.UnusablePrice
                or InventoryCommercialScenarioReason.InvalidPrice
                or InventoryCommercialScenarioReason.FloorUnavailable =>
                InventoryPromotionSuggestionReason.FinancialDataUnavailable,
            _ => null,
        };

    static IReadOnlyList<InventoryPromotionSuggestionWarning> BuildWarnings(
        bool suggested,
        InventoryCommercialScenarioResult scenario,
        bool hasWholesalePricing)
    {
        if (!suggested)
            return [];

        var warnings = new List<InventoryPromotionSuggestionWarning>(2);
        if (scenario.MinimumGrossMarginPercent is double margin
            && double.IsFinite(margin)
            && Math.Abs(margin) < 0.0000001)
            warnings.Add(InventoryPromotionSuggestionWarning.MinimumMarginPolicyAllowsAtCost);
        if (hasWholesalePricing)
            warnings.Add(InventoryPromotionSuggestionWarning.WholesalePricingMayDiffer);
        return warnings;
    }

    static IReadOnlyList<InventoryCommercialScenario> PreserveScenarios(
        IReadOnlyList<InventoryCommercialScenario>? scenarios) =>
        scenarios ?? [];

    static InventoryPromotionSuggestionReason SelectPrimary(
        List<InventoryPromotionSuggestionReason> collected)
    {
        foreach (var reason in ReasonPrecedence)
        {
            if (collected.Contains(reason))
                return reason;
        }

        return InventoryPromotionSuggestionReason.None;
    }

    static IReadOnlyList<InventoryPromotionSuggestionReason> SelectSecondary(
        List<InventoryPromotionSuggestionReason> collected,
        InventoryPromotionSuggestionReason primary)
    {
        var secondary = new List<InventoryPromotionSuggestionReason>(collected.Count);
        foreach (var reason in ReasonPrecedence)
        {
            if (reason == primary)
                continue;
            if (!collected.Contains(reason))
                continue;
            secondary.Add(reason);
        }

        return secondary;
    }

    static void Add(List<InventoryPromotionSuggestionReason> collected, InventoryPromotionSuggestionReason reason)
    {
        if (reason == InventoryPromotionSuggestionReason.None)
            return;
        if (collected.Contains(reason))
            return;
        collected.Add(reason);
    }

    static bool Has(
        List<InventoryPromotionSuggestionReason> collected,
        InventoryPromotionSuggestionReason reason) =>
        collected.Contains(reason);
}
