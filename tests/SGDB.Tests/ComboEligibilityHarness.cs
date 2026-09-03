using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

static class ComboEligibilityHarness
{
    public const int ProductId = 11;

    public static ProductTurnoverRow Turnover(
        double stock = 100,
        double fridge = 0,
        double vmv30 = 2,
        int history = 90,
        bool evidence = true,
        bool composition = false,
        bool idle = false,
        bool anomaly = false,
        bool zeroWithDemand = false,
        InventoryCoverageBand band = InventoryCoverageBand.Normal) =>
        new()
        {
            ProductId = ProductId,
            Name = "P",
            Code = "P1",
            Stock = stock,
            StockFridge = fridge,
            TotalStock = stock + fridge,
            Vmv7 = vmv30,
            Vmv30 = vmv30,
            Vmv90 = vmv30,
            HistoryDays = history,
            HasPhysicalAvailabilityEvidence = evidence,
            IsCompositionProduct = composition,
            IsIdle = idle,
            HasLocationStockAnomaly = anomaly,
            IsZeroStockWithTurnover = zeroWithDemand,
            IsHistoryInsufficient7 = history < 7,
            IsHistoryInsufficient30 = history < 30,
            IsHistoryInsufficient90 = history < 90,
            CoverageBand = band,
            CoverageDays = vmv30 > InventoryIntelligenceEngine.Epsilon
                ? (stock + fridge) / vmv30
                : null,
        };

    public static InventoryAttentionResult Attention(
        InventoryAttentionReason primary = InventoryAttentionReason.None,
        InventoryAttentionFamily family = InventoryAttentionFamily.Normal,
        InventoryOperatorAction action = InventoryOperatorAction.Monitor,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Reliable,
        double? surplus = null,
        double? excess = null,
        params InventoryAttentionReason[] secondary) =>
        new()
        {
            ProductId = ProductId,
            PrimaryReason = primary,
            Family = family,
            Action = action,
            Confidence = confidence,
            ProjectedExpirySurplusQuantity = surplus,
            ProjectedExcessQuantity = excess,
            SecondaryReasons = secondary,
        };

    public static InventoryCommercialFacts Facts(
        bool composition = false,
        bool canEvaluate = true,
        InventoryCommercialCostQuality cost = InventoryCommercialCostQuality.Known,
        InventoryCommercialPriceQuality price = InventoryCommercialPriceQuality.Usable,
        params InventoryCommercialFactsReason[] limitations) =>
        new()
        {
            ProductId = ProductId,
            ProductFound = true,
            CatalogSalePrice = 10,
            CurrentAverageCost = 6,
            PriceQuality = price,
            CostQuality = cost,
            CanEvaluateFinancialScenario = canEvaluate,
            AllowsSale = true,
            IsCompositionProduct = composition,
            LimitationReasons = limitations,
        };

    public static InventoryPurchaseGuidanceResult Guidance(
        InventoryPurchaseGuidanceAction action = InventoryPurchaseGuidanceAction.Monitor,
        InventoryPurchaseGuidanceReason primary = InventoryPurchaseGuidanceReason.None,
        InventoryPurchaseGuidanceStatus status = InventoryPurchaseGuidanceStatus.Monitor) =>
        new()
        {
            ProductId = ProductId,
            Action = action,
            PrimaryReason = primary,
            Status = status,
            Confidence = InventoryAttentionConfidence.Reliable,
            SecondaryReasons = [],
        };

    public static InventoryComboEligibilityInput Input(
        ProductTurnoverRow? turnover = null,
        InventoryAttentionResult? attention = null,
        InventoryCommercialFacts? facts = null,
        InventoryPurchaseGuidanceResult? guidance = null,
        bool includeDefaults = true) =>
        new()
        {
            Turnover = turnover ?? (includeDefaults ? Turnover() : null),
            Attention = attention ?? (includeDefaults ? Attention() : null),
            Facts = facts ?? (includeDefaults ? Facts() : null),
            Guidance = guidance ?? (includeDefaults ? Guidance() : null),
        };
}
