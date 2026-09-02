using SGDB.Domain.Common;
using SGDB.Domain.Products;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Motor puro 70F-B4B: simulação de catálogo a partir de B1/B2/B3/70C–E já calculados.
/// Sem I/O, UI, promoção, PDV, combo ou duração. QueryCount = 0.
/// Piso é referência, nunca cenário. Máximo 2 preços (leve / moderado).
/// </summary>
public static class InventoryCommercialScenarioEngine
{
    public const int ExpectedQueryCount = 0;
    public const int MarginGuardLimit = 25;

    /// <summary>
    /// Precedência 70F-B4B. Expired sempre vence. PolicyMissing não esconde Expired.
    /// </summary>
    public static readonly InventoryCommercialScenarioReason[] ReasonPrecedence =
    [
        InventoryCommercialScenarioReason.Expired,
        InventoryCommercialScenarioReason.InvalidInput,
        InventoryCommercialScenarioReason.NegativeStock,
        InventoryCommercialScenarioReason.NegativeLocationStock,
        InventoryCommercialScenarioReason.NegativeWarehouseStock,
        InventoryCommercialScenarioReason.InconsistentStockTotals,
        InventoryCommercialScenarioReason.TrackedQuantityExceedsWarehouse,
        InventoryCommercialScenarioReason.DuplicateLotId,
        InventoryCommercialScenarioReason.InvalidLotQuantity,
        InventoryCommercialScenarioReason.InvalidExpiryDate,
        InventoryCommercialScenarioReason.ProjectionMissing,
        InventoryCommercialScenarioReason.DuplicateProjection,
        InventoryCommercialScenarioReason.LocationLimitation,
        InventoryCommercialScenarioReason.InsufficientHistory,
        InventoryCommercialScenarioReason.NoPhysicalEvidence,
        InventoryCommercialScenarioReason.Undated,
        InventoryCommercialScenarioReason.NoLot,
        InventoryCommercialScenarioReason.ReviewData,
        InventoryCommercialScenarioReason.NoRecommendation,
        InventoryCommercialScenarioReason.ExpiresToday,
        InventoryCommercialScenarioReason.NearExpiryWithoutSurplus,
        InventoryCommercialScenarioReason.DatedWithoutSurplusInWindow,
        InventoryCommercialScenarioReason.HighCoverageMonitoring,
        InventoryCommercialScenarioReason.LimitedConfidence,
        InventoryCommercialScenarioReason.UnavailableConfidence,
        InventoryCommercialScenarioReason.Idle,
        InventoryCommercialScenarioReason.PolicyMissing,
        InventoryCommercialScenarioReason.PolicyInvalid,
        InventoryCommercialScenarioReason.MissingProduct,
        InventoryCommercialScenarioReason.UnknownCost,
        InventoryCommercialScenarioReason.InvalidCost,
        InventoryCommercialScenarioReason.UnusablePrice,
        InventoryCommercialScenarioReason.InvalidPrice,
        InventoryCommercialScenarioReason.NotSellable,
        InventoryCommercialScenarioReason.CompositionProduct,
        InventoryCommercialScenarioReason.AmbiguousSaleUnit,
        InventoryCommercialScenarioReason.FloorUnavailable,
        InventoryCommercialScenarioReason.PriceBelowFloor,
        InventoryCommercialScenarioReason.PriceAtFloor,
        InventoryCommercialScenarioReason.NoFinancialRoom,
        InventoryCommercialScenarioReason.ScenarioCollapsedByRounding,
        InventoryCommercialScenarioReason.ExpirySurplus,
        InventoryCommercialScenarioReason.ProjectedExcess30,
    ];

    public static InventoryCommercialScenarioResult Evaluate(
        InventoryCommercialEligibilityResult? eligibility,
        InventoryCommercialFacts? facts,
        InventoryCommercialMarginPolicyResolution? policyResolution,
        InventoryCommercialPriceFloorResult? floor,
        ProductTurnoverRow? turnover,
        InventoryProjectedProduct? projection,
        InventoryAttentionResult? attention) =>
        Evaluate(new InventoryCommercialScenarioInput
        {
            Eligibility = eligibility,
            Facts = facts,
            PolicyResolution = policyResolution,
            Floor = floor,
            Turnover = turnover,
            Projection = projection,
            Attention = attention,
        });

    public static InventoryCommercialScenarioResult Evaluate(InventoryCommercialScenarioInput? input)
    {
        input ??= new InventoryCommercialScenarioInput();
        var eligibility = input.Eligibility;
        var facts = input.Facts;
        var policy = input.PolicyResolution;
        var floor = input.Floor;
        var attention = input.Attention;
        var collected = new List<InventoryCommercialScenarioReason>(12);

        if (eligibility is null)
            Add(collected, InventoryCommercialScenarioReason.InvalidInput);

        foreach (var mapped in EnumerateEligibility(eligibility))
            Add(collected, mapped);

        foreach (var mapped in EnumerateFacts(facts))
            Add(collected, mapped);

        var confidence = eligibility?.Confidence
            ?? attention?.Confidence
            ?? InventoryAttentionConfidence.Unavailable;
        if (confidence == InventoryAttentionConfidence.Limited)
            Add(collected, InventoryCommercialScenarioReason.LimitedConfidence);
        if (confidence == InventoryAttentionConfidence.Unavailable)
            Add(collected, InventoryCommercialScenarioReason.UnavailableConfidence);

        var thesis = ResolveThesis(eligibility, attention);
        Add(collected, ReasonOfThesis(thesis));

        if (policy is null
            || policy.Status == InventoryCommercialMarginPolicyResolutionStatus.Missing)
            Add(collected, InventoryCommercialScenarioReason.PolicyMissing);
        else if (policy.Status == InventoryCommercialMarginPolicyResolutionStatus.Invalid)
            Add(collected, InventoryCommercialScenarioReason.PolicyInvalid);

        var status = ResolveStatus(
            eligibility, confidence, thesis, policy, facts, floor, collected);

        IReadOnlyList<InventoryCommercialScenario> scenarios = [];
        if (status == InventoryCommercialScenarioStatus.Available)
        {
            scenarios = BuildScenarios(facts, policy, floor);
            if (scenarios.Count == 0)
            {
                status = InventoryCommercialScenarioStatus.MonitorOnly;
                Add(collected, InventoryCommercialScenarioReason.ScenarioCollapsedByRounding);
            }
        }

        if (status != InventoryCommercialScenarioStatus.Available)
            scenarios = [];

        var primary = SelectPrimary(collected);
        var secondary = SelectSecondary(collected, primary);
        var (qty, qtySource) = AttentionQuantityOf(status, thesis, attention);

        return new InventoryCommercialScenarioResult
        {
            ProductId = ProductIdOf(input),
            Status = status,
            PrimaryReason = primary,
            SecondaryReasons = secondary,
            Thesis = status == InventoryCommercialScenarioStatus.Expired
                ? InventoryCommercialScenarioThesis.None
                : thesis,
            Confidence = confidence,
            CurrentCatalogPrice = CatalogPriceOf(facts),
            CurrentGrossMarginPercent = floor?.CurrentGrossMarginPercent,
            MinimumAllowedCatalogPrice = floor?.MinimumAllowedCatalogPrice,
            MinimumGrossMarginPercent = PolicyPercentOf(policy, floor),
            FinancialRoomAmount = RoomOf(floor),
            CatalogPriceIsAboveMinimumAllowed = floor?.CatalogPriceIsAboveMinimumAllowed == true,
            AttentionQuantity = qty,
            AttentionQuantitySource = qtySource,
            Scenarios = scenarios,
        };
    }

    static InventoryCommercialScenarioStatus ResolveStatus(
        InventoryCommercialEligibilityResult? eligibility,
        InventoryAttentionConfidence confidence,
        InventoryCommercialScenarioThesis thesis,
        InventoryCommercialMarginPolicyResolution? policy,
        InventoryCommercialFacts? facts,
        InventoryCommercialPriceFloorResult? floor,
        List<InventoryCommercialScenarioReason> collected)
    {
        if (Has(collected, InventoryCommercialScenarioReason.Expired)
            || eligibility?.PrimaryReason == InventoryCommercialEligibilityReason.Expired)
            return InventoryCommercialScenarioStatus.Expired;

        if (eligibility is null
            || eligibility.Kind == InventoryCommercialEligibilityKind.ReviewData)
            return InventoryCommercialScenarioStatus.ReviewData;

        if (eligibility.Kind == InventoryCommercialEligibilityKind.NoCommercialRecommendation)
            return InventoryCommercialScenarioStatus.NoRecommendation;

        if (eligibility.Kind == InventoryCommercialEligibilityKind.MonitorOnly)
            return InventoryCommercialScenarioStatus.MonitorOnly;

        if (confidence == InventoryAttentionConfidence.Unavailable)
            return InventoryCommercialScenarioStatus.FinancialDataUnavailable;

        if (confidence == InventoryAttentionConfidence.Limited)
            return InventoryCommercialScenarioStatus.MonitorOnly;

        if (thesis is InventoryCommercialScenarioThesis.Idle
            or InventoryCommercialScenarioThesis.HighCoverage
            or InventoryCommercialScenarioThesis.None)
            return InventoryCommercialScenarioStatus.MonitorOnly;

        if (policy is null
            || policy.Status == InventoryCommercialMarginPolicyResolutionStatus.Missing)
            return InventoryCommercialScenarioStatus.PolicyMissing;

        if (policy.Status == InventoryCommercialMarginPolicyResolutionStatus.Invalid)
            return InventoryCommercialScenarioStatus.PolicyInvalid;

        if (facts is null || !facts.ProductFound || !facts.CanEvaluateFinancialScenario)
            return InventoryCommercialScenarioStatus.FinancialDataUnavailable;

        if (floor is null
            || floor.Status != InventoryCommercialPriceFloorStatus.Available
            || floor.MinimumAllowedCatalogPrice is not double)
        {
            Add(collected, InventoryCommercialScenarioReason.FloorUnavailable);
            return InventoryCommercialScenarioStatus.FinancialDataUnavailable;
        }

        if (!floor.MeetsMinimumMargin)
        {
            Add(collected, InventoryCommercialScenarioReason.PriceBelowFloor);
            return InventoryCommercialScenarioStatus.MonitorOnly;
        }

        if (!floor.CatalogPriceIsAboveMinimumAllowed
            || floor.AmountAboveMinimumAllowedCatalogPrice <= 0)
        {
            Add(collected, floor.AmountAboveMinimumAllowedCatalogPrice <= 0
                && floor.MeetsMinimumMargin
                ? InventoryCommercialScenarioReason.PriceAtFloor
                : InventoryCommercialScenarioReason.NoFinancialRoom);
            return InventoryCommercialScenarioStatus.MonitorOnly;
        }

        if (thesis is InventoryCommercialScenarioThesis.ExpirySurplus
            or InventoryCommercialScenarioThesis.ProjectedExcess30)
            return InventoryCommercialScenarioStatus.Available;

        return InventoryCommercialScenarioStatus.MonitorOnly;
    }

    static IReadOnlyList<InventoryCommercialScenario> BuildScenarios(
        InventoryCommercialFacts? facts,
        InventoryCommercialMarginPolicyResolution? policy,
        InventoryCommercialPriceFloorResult? floor)
    {
        if (facts?.CatalogSalePrice is not double sale
            || facts.CurrentAverageCost is not double cost
            || floor?.MinimumAllowedCatalogPrice is not double floorPrice
            || policy?.EffectiveMinimumGrossMarginPercent is not decimal minDec)
            return [];

        var room = floor.AmountAboveMinimumAllowedCatalogPrice;
        if (!TryToDecimal(sale, out var s)
            || !TryToDecimal(floorPrice, out var f)
            || !TryToDecimal(room, out var r)
            || r <= 0m)
            return [];

        var minPercent = decimal.ToDouble(minDec);
        var candidates = new (InventoryCommercialScenarioKind Kind, decimal Raw)[]
        {
            (InventoryCommercialScenarioKind.Light, s - r / 3m),
            (InventoryCommercialScenarioKind.Moderate, s - 2m * r / 3m),
        };

        var scenarios = new List<InventoryCommercialScenario>(2);
        foreach (var (kind, raw) in candidates)
        {
            var scenario = TryCreateScenario(kind, raw, sale, floorPrice, cost, minPercent);
            if (scenario is null)
                continue;
            if (scenarios.Exists(existing =>
                    Cents(existing.SimulatedCatalogPrice) == Cents(scenario.SimulatedCatalogPrice)))
                continue;
            scenarios.Add(scenario);
        }

        return scenarios;
    }

    static InventoryCommercialScenario? TryCreateScenario(
        InventoryCommercialScenarioKind kind,
        decimal raw,
        double currentSale,
        double floorPrice,
        double cost,
        double minPercent)
    {
        var price = MonetaryRounding.Round((double)raw);
        var guard = 0;
        while (guard++ < MarginGuardLimit && !SatisfiesMargin(cost, price, minPercent))
        {
            price = MonetaryRounding.Round(price + 0.01);
            if (Cents(price) >= Cents(currentSale))
                return null;
        }

        if (Cents(price) <= Cents(floorPrice) || Cents(price) >= Cents(currentSale))
            return null;
        if (!SatisfiesMargin(cost, price, minPercent))
            return null;

        var reduction = MonetaryRounding.Round(currentSale - price);
        var percent = currentSale <= 0
            ? 0
            : MonetaryRounding.Round(reduction / currentSale * 100.0);
        return new InventoryCommercialScenario
        {
            Kind = kind,
            SimulatedCatalogPrice = price,
            ReductionAmount = reduction,
            ReductionPercent = percent,
            GrossMarginPercent = ProductPriceCalculator.MarginOnSale(cost, price),
        };
    }

    static bool SatisfiesMargin(double cost, double sale, double minPercent)
    {
        if (sale <= 0)
            return false;
        return ProductPriceCalculator.MarginOnSale(cost, sale) + 0.0000000001 >= minPercent;
    }

    static InventoryCommercialScenarioThesis ResolveThesis(
        InventoryCommercialEligibilityResult? eligibility,
        InventoryAttentionResult? attention)
    {
        if (HasEligibility(eligibility, InventoryCommercialEligibilityReason.Expired))
            return InventoryCommercialScenarioThesis.None;
        if (QuantityThesis(attention?.ProjectedExpirySurplusQuantity))
            return InventoryCommercialScenarioThesis.ExpirySurplus;
        if (QuantityThesis(attention?.ProjectedExcessQuantity))
            return InventoryCommercialScenarioThesis.ProjectedExcess30;
        if (HasEligibility(eligibility, InventoryCommercialEligibilityReason.Idle))
            return InventoryCommercialScenarioThesis.Idle;
        if (HasEligibility(eligibility, InventoryCommercialEligibilityReason.HighCoverageWithoutExcess))
            return InventoryCommercialScenarioThesis.HighCoverage;
        return eligibility?.PrimaryReason switch
        {
            InventoryCommercialEligibilityReason.ProjectedExpirySurplus =>
                InventoryCommercialScenarioThesis.ExpirySurplus,
            InventoryCommercialEligibilityReason.ProjectedExcess =>
                InventoryCommercialScenarioThesis.ProjectedExcess30,
            InventoryCommercialEligibilityReason.Idle => InventoryCommercialScenarioThesis.Idle,
            InventoryCommercialEligibilityReason.HighCoverageWithoutExcess =>
                InventoryCommercialScenarioThesis.HighCoverage,
            _ => InventoryCommercialScenarioThesis.None,
        };
    }

    static (double? Qty, InventoryCommercialAttentionQuantitySource Source) AttentionQuantityOf(
        InventoryCommercialScenarioStatus status,
        InventoryCommercialScenarioThesis thesis,
        InventoryAttentionResult? attention)
    {
        if (status == InventoryCommercialScenarioStatus.Expired)
            return (null, InventoryCommercialAttentionQuantitySource.None);

        if (thesis == InventoryCommercialScenarioThesis.ExpirySurplus
            && QuantityThesis(attention?.ProjectedExpirySurplusQuantity))
            return (attention!.ProjectedExpirySurplusQuantity,
                InventoryCommercialAttentionQuantitySource.ExpirySurplus);

        if (thesis == InventoryCommercialScenarioThesis.ProjectedExcess30
            && QuantityThesis(attention?.ProjectedExcessQuantity))
            return (attention!.ProjectedExcessQuantity,
                InventoryCommercialAttentionQuantitySource.ProjectedExcess30);

        return (null, InventoryCommercialAttentionQuantitySource.None);
    }

    static IEnumerable<InventoryCommercialScenarioReason> EnumerateEligibility(
        InventoryCommercialEligibilityResult? eligibility)
    {
        if (eligibility is null)
            yield break;
        if (MapEligibility(eligibility.PrimaryReason) is { } primary)
            yield return primary;
        foreach (var reason in eligibility.SecondaryReasons ?? [])
        {
            if (MapEligibility(reason) is { } mapped)
                yield return mapped;
        }
    }

    static IEnumerable<InventoryCommercialScenarioReason> EnumerateFacts(InventoryCommercialFacts? facts)
    {
        if (facts is null)
            yield break;
        foreach (var reason in facts.LimitationReasons ?? [])
        {
            if (MapFacts(reason) is { } mapped)
                yield return mapped;
        }
    }

    static InventoryCommercialScenarioReason? MapEligibility(InventoryCommercialEligibilityReason reason) =>
        reason switch
        {
            InventoryCommercialEligibilityReason.Expired => InventoryCommercialScenarioReason.Expired,
            InventoryCommercialEligibilityReason.InvalidInput =>
                InventoryCommercialScenarioReason.InvalidInput,
            InventoryCommercialEligibilityReason.NegativeStock =>
                InventoryCommercialScenarioReason.NegativeStock,
            InventoryCommercialEligibilityReason.NegativeLocationStock =>
                InventoryCommercialScenarioReason.NegativeLocationStock,
            InventoryCommercialEligibilityReason.NegativeWarehouseStock =>
                InventoryCommercialScenarioReason.NegativeWarehouseStock,
            InventoryCommercialEligibilityReason.InconsistentStockTotals =>
                InventoryCommercialScenarioReason.InconsistentStockTotals,
            InventoryCommercialEligibilityReason.TrackedQuantityExceedsWarehouse =>
                InventoryCommercialScenarioReason.TrackedQuantityExceedsWarehouse,
            InventoryCommercialEligibilityReason.DuplicateLotId =>
                InventoryCommercialScenarioReason.DuplicateLotId,
            InventoryCommercialEligibilityReason.InvalidLotQuantity =>
                InventoryCommercialScenarioReason.InvalidLotQuantity,
            InventoryCommercialEligibilityReason.InvalidExpiryDate =>
                InventoryCommercialScenarioReason.InvalidExpiryDate,
            InventoryCommercialEligibilityReason.ProjectionMissing =>
                InventoryCommercialScenarioReason.ProjectionMissing,
            InventoryCommercialEligibilityReason.DuplicateProjection =>
                InventoryCommercialScenarioReason.DuplicateProjection,
            InventoryCommercialEligibilityReason.CompositionProduct =>
                InventoryCommercialScenarioReason.CompositionProduct,
            InventoryCommercialEligibilityReason.InsufficientHistory =>
                InventoryCommercialScenarioReason.InsufficientHistory,
            InventoryCommercialEligibilityReason.NoPhysicalEvidence =>
                InventoryCommercialScenarioReason.NoPhysicalEvidence,
            InventoryCommercialEligibilityReason.LocationLimitation =>
                InventoryCommercialScenarioReason.LocationLimitation,
            InventoryCommercialEligibilityReason.Undated => InventoryCommercialScenarioReason.Undated,
            InventoryCommercialEligibilityReason.NoLot => InventoryCommercialScenarioReason.NoLot,
            InventoryCommercialEligibilityReason.ExpiresToday =>
                InventoryCommercialScenarioReason.ExpiresToday,
            InventoryCommercialEligibilityReason.NearExpiryWithoutSurplus =>
                InventoryCommercialScenarioReason.NearExpiryWithoutSurplus,
            InventoryCommercialEligibilityReason.DatedWithoutSurplusInWindow =>
                InventoryCommercialScenarioReason.DatedWithoutSurplusInWindow,
            InventoryCommercialEligibilityReason.ProjectedExpirySurplus =>
                InventoryCommercialScenarioReason.ExpirySurplus,
            InventoryCommercialEligibilityReason.ProjectedExcess =>
                InventoryCommercialScenarioReason.ProjectedExcess30,
            InventoryCommercialEligibilityReason.Idle => InventoryCommercialScenarioReason.Idle,
            InventoryCommercialEligibilityReason.HighCoverageWithoutExcess =>
                InventoryCommercialScenarioReason.HighCoverageMonitoring,
            InventoryCommercialEligibilityReason.NoObservableDemand =>
                InventoryCommercialScenarioReason.NoRecommendation,
            InventoryCommercialEligibilityReason.ZeroStock =>
                InventoryCommercialScenarioReason.NoRecommendation,
            InventoryCommercialEligibilityReason.AnalysisUnavailable =>
                InventoryCommercialScenarioReason.UnavailableConfidence,
            _ => null,
        };

    static InventoryCommercialScenarioReason? MapFacts(InventoryCommercialFactsReason reason) =>
        reason switch
        {
            InventoryCommercialFactsReason.MissingProduct =>
                InventoryCommercialScenarioReason.MissingProduct,
            InventoryCommercialFactsReason.InvalidCost => InventoryCommercialScenarioReason.InvalidCost,
            InventoryCommercialFactsReason.UnknownCost => InventoryCommercialScenarioReason.UnknownCost,
            InventoryCommercialFactsReason.InvalidSalePrice =>
                InventoryCommercialScenarioReason.InvalidPrice,
            InventoryCommercialFactsReason.UnusableSalePrice =>
                InventoryCommercialScenarioReason.UnusablePrice,
            InventoryCommercialFactsReason.SaleNotAllowed =>
                InventoryCommercialScenarioReason.NotSellable,
            InventoryCommercialFactsReason.CompositionProduct =>
                InventoryCommercialScenarioReason.CompositionProduct,
            InventoryCommercialFactsReason.AmbiguousSaleUnit =>
                InventoryCommercialScenarioReason.AmbiguousSaleUnit,
            _ => null,
        };

    static InventoryCommercialScenarioReason ReasonOfThesis(InventoryCommercialScenarioThesis thesis) =>
        thesis switch
        {
            InventoryCommercialScenarioThesis.ExpirySurplus =>
                InventoryCommercialScenarioReason.ExpirySurplus,
            InventoryCommercialScenarioThesis.ProjectedExcess30 =>
                InventoryCommercialScenarioReason.ProjectedExcess30,
            InventoryCommercialScenarioThesis.Idle => InventoryCommercialScenarioReason.Idle,
            InventoryCommercialScenarioThesis.HighCoverage =>
                InventoryCommercialScenarioReason.HighCoverageMonitoring,
            _ => InventoryCommercialScenarioReason.None,
        };

    static InventoryCommercialScenarioReason SelectPrimary(
        List<InventoryCommercialScenarioReason> collected)
    {
        foreach (var reason in ReasonPrecedence)
        {
            if (collected.Contains(reason))
                return reason;
        }

        return InventoryCommercialScenarioReason.None;
    }

    static IReadOnlyList<InventoryCommercialScenarioReason> SelectSecondary(
        List<InventoryCommercialScenarioReason> collected,
        InventoryCommercialScenarioReason primary)
    {
        var secondary = new List<InventoryCommercialScenarioReason>(collected.Count);
        foreach (var reason in ReasonPrecedence)
        {
            if (reason == primary)
                continue;
            if (collected.Contains(reason))
                secondary.Add(reason);
        }

        return secondary;
    }

    static int ProductIdOf(InventoryCommercialScenarioInput input)
    {
        if (input.Eligibility is { ProductId: > 0 } eligibility)
            return eligibility.ProductId;
        if (input.Facts is { ProductId: > 0 } facts)
            return facts.ProductId;
        if (input.Attention is { ProductId: > 0 } attention)
            return attention.ProductId;
        if (input.Turnover is { ProductId: > 0 } turnover)
            return turnover.ProductId;
        if (input.Projection is { ProductId: > 0 } projection)
            return projection.ProductId;
        return 0;
    }

    static double? CatalogPriceOf(InventoryCommercialFacts? facts) =>
        facts is { ProductFound: true } ? facts.CatalogSalePrice : null;

    static double? PolicyPercentOf(
        InventoryCommercialMarginPolicyResolution? policy,
        InventoryCommercialPriceFloorResult? floor)
    {
        if (policy?.Status == InventoryCommercialMarginPolicyResolutionStatus.Available
            && policy.EffectiveMinimumGrossMarginPercent is decimal value)
            return decimal.ToDouble(value);
        if (policy?.Status is InventoryCommercialMarginPolicyResolutionStatus.Missing
            or InventoryCommercialMarginPolicyResolutionStatus.Invalid)
            return null;
        return floor?.MinimumGrossMarginPercent;
    }

    static double? RoomOf(InventoryCommercialPriceFloorResult? floor) =>
        floor?.Status == InventoryCommercialPriceFloorStatus.Available
            ? floor.AmountAboveMinimumAllowedCatalogPrice
            : null;

    static bool HasEligibility(
        InventoryCommercialEligibilityResult? eligibility,
        InventoryCommercialEligibilityReason reason)
    {
        if (eligibility is null)
            return false;
        if (eligibility.PrimaryReason == reason)
            return true;
        foreach (var item in eligibility.SecondaryReasons ?? [])
        {
            if (item == reason)
                return true;
        }

        return false;
    }

    static bool QuantityThesis(double? quantity) =>
        quantity is double value
        && InventoryIntelligenceEngine.IsFinite(value)
        && value > InventoryIntelligenceEngine.Epsilon;

    static bool Has(List<InventoryCommercialScenarioReason> collected, InventoryCommercialScenarioReason reason) =>
        collected.Contains(reason);

    static void Add(List<InventoryCommercialScenarioReason> reasons, InventoryCommercialScenarioReason reason)
    {
        if (reason == InventoryCommercialScenarioReason.None)
            return;
        if (!reasons.Contains(reason))
            reasons.Add(reason);
    }

    static bool TryToDecimal(double value, out decimal result)
    {
        result = 0m;
        if (!InventoryIntelligenceEngine.IsFinite(value))
            return false;
        try
        {
            result = Convert.ToDecimal(value);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    static long Cents(double value) =>
        (long)Math.Round(value * 100.0, MidpointRounding.AwayFromZero);
}
