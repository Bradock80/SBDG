using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Motor puro 70G-B1: orientação qualitativa de reposição a partir de 70C/70D.
/// Sem I/O, SQL, UI ou recálculo das autoridades.
/// </summary>
public static class InventoryPurchaseGuidanceEngine
{
    public const int ExpectedQueryCount = 0;

    public const double Epsilon = InventoryIntelligenceEngine.Epsilon;

    /// <summary>
    /// Precedência determinística do PrimaryReason (contrato 70G-A2).
    /// Conflitos impossíveis Excess+Low/Critical e Idle+Low/Critical
    /// viram StructuralDataIssue antes desta lista.
    /// </summary>
    public static readonly InventoryPurchaseGuidanceReason[] ReasonPrecedence =
    [
        InventoryPurchaseGuidanceReason.CompositionProduct,
        InventoryPurchaseGuidanceReason.StructuralDataIssue,
        InventoryPurchaseGuidanceReason.NoPhysicalEvidence,
        InventoryPurchaseGuidanceReason.Expired,
        InventoryPurchaseGuidanceReason.ExpiresToday,
        InventoryPurchaseGuidanceReason.ProjectedExcess30,
        InventoryPurchaseGuidanceReason.ProjectedExpirySurplus,
        InventoryPurchaseGuidanceReason.IdleStock,
        InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand,
        InventoryPurchaseGuidanceReason.CriticalCoverage,
        InventoryPurchaseGuidanceReason.LowCoverage,
        InventoryPurchaseGuidanceReason.NoObservableDemand,
        InventoryPurchaseGuidanceReason.InsufficientHistory,
        InventoryPurchaseGuidanceReason.LocationLimitation,
    ];

    public static InventoryPurchaseGuidanceResult Evaluate(InventoryPurchaseGuidanceInput? input)
    {
        input ??= new InventoryPurchaseGuidanceInput();

        if (HasNonFiniteOperationalNumbers(input))
            return Finish(input.ProductId, InventoryPurchaseGuidanceReason.StructuralDataIssue, []);

        var collected = Collect(input);

        if (collected.Contains(InventoryPurchaseGuidanceReason.CompositionProduct))
            return Finish(input.ProductId, InventoryPurchaseGuidanceReason.CompositionProduct, []);

        if (HasImpossibleExcessCoverageConflict(input) || HasImpossibleIdleCoverageConflict(input))
            return Finish(input.ProductId, InventoryPurchaseGuidanceReason.StructuralDataIssue, []);

        var primary = SelectPrimary(collected);
        var secondary = SelectSecondary(collected, primary);
        return Finish(input.ProductId, primary, secondary, input);
    }

    static InventoryPurchaseGuidanceResult Finish(
        int productId,
        InventoryPurchaseGuidanceReason primary,
        IReadOnlyList<InventoryPurchaseGuidanceReason> secondary,
        InventoryPurchaseGuidanceInput? input = null)
    {
        var action = ActionOf(primary);
        return new InventoryPurchaseGuidanceResult
        {
            ProductId = productId,
            Status = StatusOf(action),
            Action = action,
            Confidence = ConfidenceOf(action, primary, input),
            PrimaryReason = primary,
            SecondaryReasons = secondary,
        };
    }

    static HashSet<InventoryPurchaseGuidanceReason> Collect(InventoryPurchaseGuidanceInput input)
    {
        var set = new HashSet<InventoryPurchaseGuidanceReason>();

        if (IsComposition(input))
            set.Add(InventoryPurchaseGuidanceReason.CompositionProduct);

        if (HasStructuralIssue(input))
            set.Add(InventoryPurchaseGuidanceReason.StructuralDataIssue);

        if (!input.HasPhysicalAvailabilityEvidence)
            set.Add(InventoryPurchaseGuidanceReason.NoPhysicalEvidence);

        if (input.HasExpiredLot)
            set.Add(InventoryPurchaseGuidanceReason.Expired);

        if (input.HasExpiresTodayLot)
            set.Add(InventoryPurchaseGuidanceReason.ExpiresToday);

        if (HasValidExcess30(input))
            set.Add(InventoryPurchaseGuidanceReason.ProjectedExcess30);

        if (HasReliableExpirySurplus(input))
            set.Add(InventoryPurchaseGuidanceReason.ProjectedExpirySurplus);

        if (input.IsIdle && GreaterThanEpsilon(input.TotalStock))
            set.Add(InventoryPurchaseGuidanceReason.IdleStock);

        if (input.IsZeroStockWithTurnover)
            set.Add(InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand);

        if (input.HasPhysicalAvailabilityEvidence
            && input.CoverageBand == InventoryCoverageBand.Critical)
            set.Add(InventoryPurchaseGuidanceReason.CriticalCoverage);

        if (input.HasPhysicalAvailabilityEvidence
            && input.CoverageBand == InventoryCoverageBand.Low)
            set.Add(InventoryPurchaseGuidanceReason.LowCoverage);

        if (input.HasPhysicalAvailabilityEvidence
            && !input.IsIdle
            && ApproximatelyZero(input.Vmv30))
            set.Add(InventoryPurchaseGuidanceReason.NoObservableDemand);

        if (input.HasPhysicalAvailabilityEvidence && HistoryInsufficient(input))
            set.Add(InventoryPurchaseGuidanceReason.InsufficientHistory);

        if (input.HasLotLocationLimitation)
            set.Add(InventoryPurchaseGuidanceReason.LocationLimitation);

        return set;
    }

    static InventoryPurchaseGuidanceReason SelectPrimary(
        HashSet<InventoryPurchaseGuidanceReason> collected)
    {
        foreach (var reason in ReasonPrecedence)
        {
            if (collected.Contains(reason))
                return reason;
        }

        return InventoryPurchaseGuidanceReason.None;
    }

    static IReadOnlyList<InventoryPurchaseGuidanceReason> SelectSecondary(
        HashSet<InventoryPurchaseGuidanceReason> collected,
        InventoryPurchaseGuidanceReason primary)
    {
        if (primary is InventoryPurchaseGuidanceReason.CompositionProduct
            or InventoryPurchaseGuidanceReason.StructuralDataIssue
            or InventoryPurchaseGuidanceReason.NoPhysicalEvidence)
            return [];

        var skipCoverageNoise = primary is InventoryPurchaseGuidanceReason.Expired
            or InventoryPurchaseGuidanceReason.ExpiresToday;

        var list = new List<InventoryPurchaseGuidanceReason>();
        foreach (var reason in ReasonPrecedence)
        {
            if (reason == primary)
                continue;
            if (!collected.Contains(reason))
                continue;
            if (skipCoverageNoise
                && reason is InventoryPurchaseGuidanceReason.CriticalCoverage
                    or InventoryPurchaseGuidanceReason.LowCoverage
                    or InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand
                    or InventoryPurchaseGuidanceReason.InsufficientHistory
                    or InventoryPurchaseGuidanceReason.NoObservableDemand
                    or InventoryPurchaseGuidanceReason.LocationLimitation)
                continue;

            list.Add(reason);
        }

        return list;
    }

    static InventoryPurchaseGuidanceAction ActionOf(InventoryPurchaseGuidanceReason primary) =>
        primary switch
        {
            InventoryPurchaseGuidanceReason.CompositionProduct =>
                InventoryPurchaseGuidanceAction.None,
            InventoryPurchaseGuidanceReason.StructuralDataIssue
                or InventoryPurchaseGuidanceReason.NoPhysicalEvidence =>
                InventoryPurchaseGuidanceAction.ReviewData,
            InventoryPurchaseGuidanceReason.Expired
                or InventoryPurchaseGuidanceReason.ExpiresToday
                or InventoryPurchaseGuidanceReason.ProjectedExcess30
                or InventoryPurchaseGuidanceReason.ProjectedExpirySurplus
                or InventoryPurchaseGuidanceReason.IdleStock =>
                InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand
                or InventoryPurchaseGuidanceReason.CriticalCoverage
                or InventoryPurchaseGuidanceReason.LowCoverage =>
                InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            InventoryPurchaseGuidanceReason.NoObservableDemand
                or InventoryPurchaseGuidanceReason.InsufficientHistory
                or InventoryPurchaseGuidanceReason.LocationLimitation
                or InventoryPurchaseGuidanceReason.None =>
                InventoryPurchaseGuidanceAction.Monitor,
            _ => InventoryPurchaseGuidanceAction.ReviewData,
        };

    static InventoryPurchaseGuidanceStatus StatusOf(InventoryPurchaseGuidanceAction action) =>
        action switch
        {
            InventoryPurchaseGuidanceAction.ConsiderReplenishment
                or InventoryPurchaseGuidanceAction.DoNotReplenishNow =>
                InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.Monitor =>
                InventoryPurchaseGuidanceStatus.Monitor,
            InventoryPurchaseGuidanceAction.ReviewData =>
                InventoryPurchaseGuidanceStatus.ReviewData,
            _ => InventoryPurchaseGuidanceStatus.NotApplicable,
        };

    static InventoryAttentionConfidence ConfidenceOf(
        InventoryPurchaseGuidanceAction action,
        InventoryPurchaseGuidanceReason primary,
        InventoryPurchaseGuidanceInput? input)
    {
        if (action is InventoryPurchaseGuidanceAction.None
            or InventoryPurchaseGuidanceAction.ReviewData)
            return InventoryAttentionConfidence.Unavailable;

        if (action == InventoryPurchaseGuidanceAction.ConsiderReplenishment)
            return InventoryAttentionConfidence.Limited;

        if (action == InventoryPurchaseGuidanceAction.DoNotReplenishNow)
            return InventoryAttentionConfidence.Reliable;

        if (primary is InventoryPurchaseGuidanceReason.NoObservableDemand
            or InventoryPurchaseGuidanceReason.InsufficientHistory
            or InventoryPurchaseGuidanceReason.LocationLimitation)
            return InventoryAttentionConfidence.Limited;

        if (input is not null
            && input.CoverageBand == InventoryCoverageBand.Normal
            && input.HasPhysicalAvailabilityEvidence
            && !HistoryInsufficient(input)
            && !input.HasLotLocationLimitation)
            return InventoryAttentionConfidence.Reliable;

        return InventoryAttentionConfidence.Limited;
    }

    static bool IsComposition(InventoryPurchaseGuidanceInput input) =>
        input.IsCompositionProduct
        || input.SkuBlockedReason == InventorySkuProjectionBlockedReason.CompositionProduct
        || input.ExpiryBlockedReason == InventoryExpiryProjectionBlockedReason.CompositionProduct;

    static bool HasStructuralIssue(InventoryPurchaseGuidanceInput input)
    {
        if (input.IsInvalidInput)
            return true;
        if (input.CoverageBand == InventoryCoverageBand.Negative)
            return true;
        if (LessThanNegativeEpsilon(input.TotalStock)
            || LessThanNegativeEpsilon(input.Stock)
            || LessThanNegativeEpsilon(input.StockFridge))
            return true;
        if (input.HasLocationStockAnomaly)
            return true;
        if (HasInconsistentStockTotalsFromValues(input))
            return true;
        if (input.HasTrackedQuantityExceedsWarehouse)
            return true;
        if (input.HasInvalidLotQuantity)
            return true;
        if (input.HasDuplicateLot)
            return true;
        if (input.HasInvalidExpiry)
            return true;

        return IsStructuralSkuBlock(input.SkuBlockedReason)
            || IsStructuralExpiryBlock(input.ExpiryBlockedReason);
    }

    static bool HasInconsistentStockTotalsFromValues(InventoryPurchaseGuidanceInput input)
    {
        if (!InventoryIntelligenceEngine.IsFinite(input.Stock)
            || !InventoryIntelligenceEngine.IsFinite(input.StockFridge)
            || !InventoryIntelligenceEngine.IsFinite(input.TotalStock))
            return false;
        return Math.Abs(input.Stock + input.StockFridge - input.TotalStock) > Epsilon;
    }

    static bool IsStructuralSkuBlock(InventorySkuProjectionBlockedReason reason) =>
        reason is InventorySkuProjectionBlockedReason.InvalidInput
            or InventorySkuProjectionBlockedReason.NegativeStock
            or InventorySkuProjectionBlockedReason.NegativeLocationStock
            or InventorySkuProjectionBlockedReason.InconsistentStockTotals;

    static bool IsStructuralExpiryBlock(InventoryExpiryProjectionBlockedReason reason) =>
        reason is InventoryExpiryProjectionBlockedReason.InvalidInput
            or InventoryExpiryProjectionBlockedReason.NegativeWarehouseStock
            or InventoryExpiryProjectionBlockedReason.NegativeLocationStock
            or InventoryExpiryProjectionBlockedReason.InconsistentStockTotals
            or InventoryExpiryProjectionBlockedReason.DuplicateLotId
            or InventoryExpiryProjectionBlockedReason.InvalidLotQuantity
            or InventoryExpiryProjectionBlockedReason.TrackedQuantityExceedsWarehouse
            or InventoryExpiryProjectionBlockedReason.InvalidExpiryDate;

    static bool HasValidExcess30(InventoryPurchaseGuidanceInput input) =>
        input.CanProjectSku && GreaterThanEpsilon(input.ProjectedExcessQuantity);

    static bool HasReliableExpirySurplus(InventoryPurchaseGuidanceInput input)
    {
        if (!GreaterThanEpsilon(input.ProjectedExpirySurplus))
            return false;
        if (input.HasLotLocationLimitation)
            return false;
        return input.ExpiryBlockedReason == InventoryExpiryProjectionBlockedReason.None;
    }

    static bool HasImpossibleExcessCoverageConflict(InventoryPurchaseGuidanceInput input) =>
        HasValidExcess30(input)
        && input.CoverageBand is InventoryCoverageBand.Low or InventoryCoverageBand.Critical;

    static bool HasImpossibleIdleCoverageConflict(InventoryPurchaseGuidanceInput input) =>
        input.IsIdle
        && input.CoverageBand is InventoryCoverageBand.Low or InventoryCoverageBand.Critical;

    static bool HistoryInsufficient(InventoryPurchaseGuidanceInput input) =>
        input.IsHistoryInsufficient30
        || input.HistoryDays < InventoryIntelligenceEngine.Window30;

    static bool HasNonFiniteOperationalNumbers(InventoryPurchaseGuidanceInput input) =>
        !InventoryIntelligenceEngine.IsFinite(input.Stock)
        || !InventoryIntelligenceEngine.IsFinite(input.StockFridge)
        || !InventoryIntelligenceEngine.IsFinite(input.TotalStock)
        || !InventoryIntelligenceEngine.IsFinite(input.Vmv30)
        || (input.CoverageDays is double days && !InventoryIntelligenceEngine.IsFinite(days))
        || (input.ProjectedExcessQuantity is double excess && !InventoryIntelligenceEngine.IsFinite(excess))
        || (input.ProjectedExpirySurplus is double surplus && !InventoryIntelligenceEngine.IsFinite(surplus));

    static bool GreaterThanEpsilon(double? value) =>
        value is double v
        && InventoryIntelligenceEngine.IsFinite(v)
        && v > Epsilon;

    static bool ApproximatelyZero(double value) =>
        InventoryIntelligenceEngine.IsFinite(value) && Math.Abs(value) <= Epsilon;

    static bool LessThanNegativeEpsilon(double value) =>
        InventoryIntelligenceEngine.IsFinite(value) && value < -Epsilon;
}
