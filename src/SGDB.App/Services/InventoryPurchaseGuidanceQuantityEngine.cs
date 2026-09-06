using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Quantidade acionável da reposição. Não decide se comprar: só preenche números
/// quando 70G-B1 já apontou ConsiderReplenishment. Sem I/O. QueryCount = 0.
/// </summary>
public static class InventoryPurchaseGuidanceQuantityEngine
{
    public const int ExpectedQueryCount = 0;
    public const double TargetCoverageDays = InventoryIntelligenceEngine.LowCoverageDaysThreshold;
    public const int ReevaluationDays = 7;
    public const int BuySoonLeadDays = 3;

    public static InventoryPurchaseGuidanceResult Attach(
        InventoryPurchaseGuidanceResult result,
        ProductTurnoverRow? turnover,
        InventoryCommercialFacts? facts = null,
        DateTime? today = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Action != InventoryPurchaseGuidanceAction.ConsiderReplenishment)
            return WithUrgency(result, InventoryPurchaseGuidanceUrgency.None);

        var day = (today ?? DateTime.Today).Date;
        var stock = turnover?.TotalStock ?? 0;
        var vmv = turnover?.Vmv30 ?? 0;
        var minStock = turnover?.MinStock ?? 0;
        var pack = turnover is { PackFactor: >= 2 } ? turnover.PackFactor : 1;
        if (!InventoryIntelligenceEngine.IsFinite(stock))
            stock = 0;
        if (!InventoryIntelligenceEngine.IsFinite(vmv) || vmv < 0)
            vmv = 0;
        if (!InventoryIntelligenceEngine.IsFinite(minStock) || minStock < 0)
            minStock = 0;
        if (!InventoryIntelligenceEngine.IsFinite(pack) || pack < 1)
            pack = 1;

        var targetFromCoverage = vmv > InventoryIntelligenceEngine.Epsilon
            ? vmv * TargetCoverageDays
            : 0;
        var targetStock = Math.Max(minStock, targetFromCoverage);
        var needed = Math.Max(0, targetStock - Math.Max(0, stock));
        if (needed <= InventoryIntelligenceEngine.Epsilon)
            needed = pack >= 2 ? pack : 1;

        var packs = 0;
        double qty;
        if (pack >= 2)
        {
            packs = (int)Math.Ceiling(needed / pack - InventoryIntelligenceEngine.Epsilon);
            if (packs < 1)
                packs = 1;
            qty = packs * pack;
        }
        else
        {
            qty = Math.Ceiling(needed - InventoryIntelligenceEngine.Epsilon);
            if (qty < 1)
                qty = 1;
        }

        var coverageAfter = vmv > InventoryIntelligenceEngine.Epsilon
            ? (Math.Max(0, stock) + qty) / vmv
            : (double?)null;
        var urgency = result.PrimaryReason is InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand
            or InventoryPurchaseGuidanceReason.CriticalCoverage
            ? InventoryPurchaseGuidanceUrgency.BuyNow
            : InventoryPurchaseGuidanceUrgency.BuySoon;

        DateOnly? orderDate = DateOnly.FromDateTime(day);
        if (urgency == InventoryPurchaseGuidanceUrgency.BuySoon
            && turnover?.CoverageDays is double days
            && InventoryIntelligenceEngine.IsFinite(days)
            && days > BuySoonLeadDays)
        {
            orderDate = DateOnly.FromDateTime(day.AddDays(Math.Max(0, days - BuySoonLeadDays)));
        }

        double? cost = null;
        if (facts?.CurrentAverageCost is double unit
            && facts.CostQuality == InventoryCommercialCostQuality.Known
            && InventoryIntelligenceEngine.IsFinite(unit)
            && unit > 0)
        {
            cost = Math.Round(unit * qty, 2, MidpointRounding.AwayFromZero);
        }

        return new InventoryPurchaseGuidanceResult
        {
            ProductId = result.ProductId,
            Status = result.Status,
            Action = result.Action,
            Confidence = result.Confidence,
            PrimaryReason = result.PrimaryReason,
            SecondaryReasons = result.SecondaryReasons,
            Urgency = urgency,
            RecommendedQuantity = qty,
            PackFactor = pack >= 2 ? pack : null,
            PackCount = pack >= 2 ? packs : null,
            EstimatedCost = cost,
            CoverageAfterPurchaseDays = coverageAfter,
            RecommendedOrderDate = orderDate,
            ReevaluationDate = DateOnly.FromDateTime(day.AddDays(ReevaluationDays)),
        };
    }

    static InventoryPurchaseGuidanceResult WithUrgency(
        InventoryPurchaseGuidanceResult result,
        InventoryPurchaseGuidanceUrgency urgency) =>
        new()
        {
            ProductId = result.ProductId,
            Status = result.Status,
            Action = result.Action,
            Confidence = result.Confidence,
            PrimaryReason = result.PrimaryReason,
            SecondaryReasons = result.SecondaryReasons,
            Urgency = urgency,
            RecommendedQuantity = result.RecommendedQuantity,
            PackFactor = result.PackFactor,
            PackCount = result.PackCount,
            EstimatedCost = result.EstimatedCost,
            CoverageAfterPurchaseDays = result.CoverageAfterPurchaseDays,
            RecommendedOrderDate = result.RecommendedOrderDate,
            ReevaluationDate = result.ReevaluationDate,
        };
}
