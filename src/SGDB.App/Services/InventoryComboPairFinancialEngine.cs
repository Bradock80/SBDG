using SGDB.Domain.Common;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Motor puro 71A-B3: fatos financeiros do par target+âncora.
/// Reusa o piso 70F. Sem SQL, UI, B1, B2, ranking ou kit.
/// QueryCount = 0.
/// </summary>
public static class InventoryComboPairFinancialEngine
{
    public const int ExpectedQueryCount = 0;

    public static InventoryComboPairFinancialFacts Evaluate(InventoryComboPairFinancialInput? input)
    {
        var targetEval = InventoryCommercialPriceFloorEngine.Evaluate(
            input?.TargetFacts, input?.MinGrossMarginPolicy);
        if (targetEval.Status is InventoryCommercialPriceFloorStatus.PolicyMissing
            or InventoryCommercialPriceFloorStatus.PolicyInvalid)
        {
            return Unavailable(InventoryComboPairFinancialReason.MarginPolicyUnavailable);
        }

        if (targetEval.Status != InventoryCommercialPriceFloorStatus.Available
            || targetEval.MinimumAllowedCatalogPrice is not double targetFloor
            || !InventoryIntelligenceEngine.IsFinite(targetFloor))
        {
            return Unavailable(InventoryComboPairFinancialReason.TargetFinancialUnavailable);
        }

        var anchorEval = InventoryCommercialPriceFloorEngine.Evaluate(
            input?.AnchorFacts, input?.MinGrossMarginPolicy);
        if (anchorEval.Status != InventoryCommercialPriceFloorStatus.Available)
            return Unavailable(InventoryComboPairFinancialReason.AnchorFinancialUnavailable);

        var target = input!.TargetFacts!;
        var anchor = input.AnchorFacts!;
        if (!InventoryCommercialPriceFloorEngine.TryToDecimal(target.CurrentAverageCost, out var targetCost)
            || !InventoryCommercialPriceFloorEngine.TryToDecimal(anchor.CurrentAverageCost, out var anchorCost)
            || !InventoryCommercialPriceFloorEngine.TryToDecimal(target.CatalogSalePrice, out _)
            || !InventoryCommercialPriceFloorEngine.TryToDecimal(anchor.CatalogSalePrice, out _)
            || !InventoryCommercialPriceFloorEngine.TryToDecimal(
                targetEval.MinimumGrossMarginPercent, out var minPercent))
        {
            return Unavailable(InventoryComboPairFinancialReason.InvalidPairValues);
        }

        var targetCatalog = target.CatalogSalePrice!.Value;
        var anchorCatalog = anchor.CatalogSalePrice!.Value;
        var normal = MonetaryRounding.Round(targetCatalog + anchorCatalog);
        var pairCostRaw = (double)(targetCost + anchorCost);
        var pairCost = MonetaryRounding.Round(pairCostRaw);
        var pairFloor = (double)InventoryCommercialPriceFloorEngine.ComputeFloor(
            targetCost + anchorCost, minPercent);

        if (!InventoryIntelligenceEngine.IsFinite(normal)
            || !InventoryIntelligenceEngine.IsFinite(pairCost)
            || !InventoryIntelligenceEngine.IsFinite(pairFloor)
            || normal <= 0
            || pairCost < 0
            || pairFloor <= 0)
        {
            return Unavailable(InventoryComboPairFinancialReason.InvalidPairValues);
        }

        var referenceCandidate = MonetaryRounding.Round(anchorCatalog + targetFloor);
        var pairReference = InventoryCommercialPriceFloorEngine.ToCents(referenceCandidate)
            >= InventoryCommercialPriceFloorEngine.ToCents(pairFloor)
            ? referenceCandidate
            : pairFloor;

        if (InventoryCommercialPriceFloorEngine.ToCents(normal)
            < InventoryCommercialPriceFloorEngine.ToCents(pairFloor))
        {
            return new InventoryComboPairFinancialFacts
            {
                Status = InventoryComboPairFinancialStatus.Unavailable,
                Reason = InventoryComboPairFinancialReason.PriceBelowFloor,
                NormalPairPrice = normal,
                PairCost = pairCost,
                PairFloorPrice = pairFloor,
                TargetFloorPrice = targetFloor,
            };
        }

        var scenarios = new List<InventoryComboPairFinancialScenario>(2)
        {
            CreateScenario(
                InventoryComboPairFinancialScenarioKind.CurrentPrices,
                normal,
                pairCost,
                normal),
        };

        if (InventoryCommercialPriceFloorEngine.ToCents(pairReference)
            < InventoryCommercialPriceFloorEngine.ToCents(normal))
        {
            scenarios.Add(CreateScenario(
                InventoryComboPairFinancialScenarioKind.TargetReductionReference,
                pairReference,
                pairCost,
                normal));
        }

        return new InventoryComboPairFinancialFacts
        {
            Status = InventoryComboPairFinancialStatus.Available,
            Reason = InventoryComboPairFinancialReason.None,
            NormalPairPrice = normal,
            PairCost = pairCost,
            PairFloorPrice = pairFloor,
            TargetFloorPrice = targetFloor,
            Scenarios = scenarios,
        };
    }

    static InventoryComboPairFinancialFacts Unavailable(InventoryComboPairFinancialReason reason) =>
        new()
        {
            Status = InventoryComboPairFinancialStatus.Unavailable,
            Reason = reason,
        };

    static InventoryComboPairFinancialScenario CreateScenario(
        InventoryComboPairFinancialScenarioKind kind,
        double pairPrice,
        double pairCost,
        double normalPairPrice)
    {
        var profit = MonetaryRounding.Round(pairPrice - pairCost);
        var margin = pairPrice > 0 ? profit / pairPrice : 0;
        var reduction = kind == InventoryComboPairFinancialScenarioKind.CurrentPrices
            ? 0
            : MonetaryRounding.Round(normalPairPrice - pairPrice);
        return new InventoryComboPairFinancialScenario
        {
            Kind = kind,
            PairPrice = pairPrice,
            GrossProfit = profit,
            GrossMargin = margin,
            ReductionFromCurrent = reduction,
        };
    }
}
