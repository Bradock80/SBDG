using SGDB.Domain.Common;
using SGDB.Domain.Products;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Motor puro 70F-B3: piso de catálogo para margem bruta sobre venda.
/// Fórmula custo / (1 − margem/100), com teto em centavos para não violar a política
/// (AwayFromZero do domínio pode ficar abaixo da margem mínima).
/// Sem SQL, UI, promoção, desconto ou margem default.
/// </summary>
public static class InventoryCommercialPriceFloorEngine
{
    public const int ExpectedQueryCount = 0;

    public static InventoryCommercialPriceFloorResult Evaluate(
        InventoryCommercialFacts? facts,
        InventoryCommercialMarginPolicy? policy)
    {
        var productId = facts?.ProductId ?? 0;
        var currentMargin = TryCurrentGrossMargin(facts);
        var policyState = ClassifyPolicy(policy, out var minPercent);

        if (policyState == InventoryCommercialPriceFloorStatus.PolicyMissing)
            return Incomplete(productId, facts, currentMargin, InventoryCommercialPriceFloorStatus.PolicyMissing);

        if (policyState == InventoryCommercialPriceFloorStatus.PolicyInvalid)
            return Incomplete(productId, facts, currentMargin, InventoryCommercialPriceFloorStatus.PolicyInvalid);

        if (facts is null || !facts.ProductFound)
            return Incomplete(
                productId,
                facts,
                currentMargin,
                InventoryCommercialPriceFloorStatus.CommercialFactsUnavailable,
                minPercent);

        if (!facts.CanEvaluateFinancialScenario)
            return Incomplete(
                productId,
                facts,
                currentMargin,
                InventoryCommercialPriceFloorStatus.FinancialScenarioUnavailable,
                minPercent);

        if (!TryToDecimal(facts.CurrentAverageCost, out var cost)
            || !TryToDecimal(facts.CatalogSalePrice, out var sale)
            || !TryToDecimal(minPercent, out var minDec))
        {
            return Incomplete(
                productId,
                facts,
                currentMargin,
                InventoryCommercialPriceFloorStatus.FinancialScenarioUnavailable,
                minPercent);
        }

        var floor = ComputeFloor(cost, minDec);
        var saleCents = ToCents(MonetaryRounding.Round((double)sale));
        var floorCents = ToCents((double)floor);
        var meets = saleCents >= floorCents;
        var above = saleCents > floorCents;
        var amountAbove = above
            ? MonetaryRounding.Round((saleCents - floorCents) / 100.0)
            : 0;
        var reasons = meets
            ? Array.Empty<InventoryCommercialPriceFloorReason>()
            : [InventoryCommercialPriceFloorReason.CurrentPriceBelowMinimumMargin];

        return new InventoryCommercialPriceFloorResult
        {
            ProductId = productId,
            Status = InventoryCommercialPriceFloorStatus.Available,
            MinimumGrossMarginPercent = minPercent,
            CatalogSalePrice = facts.CatalogSalePrice,
            CurrentAverageCost = facts.CurrentAverageCost,
            CurrentGrossMarginPercent = currentMargin,
            MinimumAllowedCatalogPrice = (double)floor,
            MeetsMinimumMargin = meets,
            CatalogPriceIsAboveMinimumAllowed = above,
            AmountAboveMinimumAllowedCatalogPrice = amountAbove,
            Reasons = reasons,
        };
    }

    static InventoryCommercialPriceFloorResult Incomplete(
        int productId,
        InventoryCommercialFacts? facts,
        double? currentMargin,
        InventoryCommercialPriceFloorStatus status,
        double? minPercent = null) =>
        new()
        {
            ProductId = productId,
            Status = status,
            MinimumGrossMarginPercent = minPercent,
            CatalogSalePrice = facts?.CatalogSalePrice,
            CurrentAverageCost = facts?.CurrentAverageCost,
            CurrentGrossMarginPercent = currentMargin,
            Reasons = [ReasonOf(status)],
        };

    static InventoryCommercialPriceFloorStatus ClassifyPolicy(
        InventoryCommercialMarginPolicy? policy,
        out double minPercent)
    {
        minPercent = 0;
        if (policy?.MinimumGrossMarginPercent is not double raw)
            return InventoryCommercialPriceFloorStatus.PolicyMissing;
        if (!InventoryIntelligenceEngine.IsFinite(raw) || raw < 0 || raw >= 100)
            return InventoryCommercialPriceFloorStatus.PolicyInvalid;
        minPercent = raw;
        return InventoryCommercialPriceFloorStatus.Available;
    }

    static double? TryCurrentGrossMargin(InventoryCommercialFacts? facts)
    {
        if (facts is null)
            return null;
        if (facts.CostQuality != InventoryCommercialCostQuality.Known)
            return null;
        if (facts.PriceQuality != InventoryCommercialPriceQuality.Usable)
            return null;
        if (facts.CurrentAverageCost is not double cost
            || facts.CatalogSalePrice is not double sale
            || !InventoryIntelligenceEngine.IsFinite(cost)
            || !InventoryIntelligenceEngine.IsFinite(sale))
            return null;
        return ProductPriceCalculator.MarginOnSale(cost, sale);
    }

    /// <summary>
    /// Única fórmula de piso 70F. Exposta internamente para o par 71A-B3
    /// (custo conjunto) sem duplicar custo/(1 − margem).
    /// </summary>
    internal static decimal ComputeFloor(decimal cost, decimal minPercent)
    {
        var exact = cost / (1m - minPercent / 100m);
        var floor = MonetaryRounding.CeilingToCents(exact);
        var guard = 0;
        while (guard++ < 25 && !SatisfiesPolicy(cost, floor, minPercent))
            floor += 0.01m;
        return floor;
    }

    static bool SatisfiesPolicy(decimal cost, decimal sale, decimal minPercent)
    {
        if (sale <= 0m)
            return false;
        return (sale - cost) / sale * 100m + 0.0000000001m >= minPercent;
    }

    static InventoryCommercialPriceFloorReason ReasonOf(InventoryCommercialPriceFloorStatus status) =>
        status switch
        {
            InventoryCommercialPriceFloorStatus.PolicyMissing =>
                InventoryCommercialPriceFloorReason.PolicyMissing,
            InventoryCommercialPriceFloorStatus.PolicyInvalid =>
                InventoryCommercialPriceFloorReason.PolicyInvalid,
            InventoryCommercialPriceFloorStatus.FinancialScenarioUnavailable =>
                InventoryCommercialPriceFloorReason.FinancialScenarioUnavailable,
            _ => InventoryCommercialPriceFloorReason.CommercialFactsUnavailable,
        };

    internal static bool TryToDecimal(double? value, out decimal result)
    {
        result = 0m;
        if (value is not double number || !InventoryIntelligenceEngine.IsFinite(number))
            return false;
        try
        {
            result = Convert.ToDecimal(number);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    internal static long ToCents(double value) =>
        (long)Math.Round(value * 100.0, MidpointRounding.AwayFromZero);
}
