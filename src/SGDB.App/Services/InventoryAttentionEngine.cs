using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Motor puro 70E-B1: prioridade de atenção a partir de 70C/70D já calculados.
/// Sem I/O, UI, RPC, promoção ou preço. Sem score 0–100.
/// Não recalcula VMV, cobertura, FEFO nem IsIdle. Sem ação comercial reservada do 70B2.
///
/// Política de colisão: o motivo de maior precedência é PrimaryReason; os demais
/// permanecem em SecondaryReasons (incluindo vencido se o dado for PrimaryReason).
/// </summary>
public static class InventoryAttentionEngine
{
    /// <summary>Reusa o epsilon físico 70C/70D. Quantidade ≤ epsilon não gera excesso.</summary>
    public const double Epsilon = InventoryIntelligenceEngine.Epsilon;

    /// <summary>Prazo curto sem sobra: DaysUntilExpiry ≤ 7 → Medium / PrioritizeSale.</summary>
    public const int NearExpiryDays = InventoryIntelligenceEngine.Window7;

    /// <summary>Acompanhar validade sem sobra: 7 &lt; dias ≤ 30. Acima de 30 não entra na 70E.</summary>
    public const int MonitorExpiryDays = InventoryIntelligenceEngine.Window30;

    /// <summary>
    /// Preparado para tendência VMV7 vs VMV30. Não altera Priority nesta B1.
    /// </summary>
    public const double VmvRecentAccelerationRatio = 1.25;

    /// <summary>Preparada. Não altera Priority nesta B1.</summary>
    public const double VmvRecentDecelerationRatio = 0.5;

    /// <summary>Preparada (VMV30 vs VMV90). Não altera Priority nesta B1.</summary>
    public const double Vmv30ExceptionalVs90Ratio = 1.5;

    /// <summary>
    /// Precedência determinística do PrimaryReason. Data estrutural antes de validade
    /// para não agir sobre quantidade inconfiável; vencido observável permanece visível.
    /// </summary>
    public static readonly InventoryAttentionReason[] ReasonPrecedence =
    [
        InventoryAttentionReason.InvalidInput,
        InventoryAttentionReason.NegativeStock,
        InventoryAttentionReason.NegativeLocationStock,
        InventoryAttentionReason.NegativeWarehouseStock,
        InventoryAttentionReason.InconsistentStockTotals,
        InventoryAttentionReason.TrackedQuantityExceedsWarehouse,
        InventoryAttentionReason.DuplicateLotId,
        InventoryAttentionReason.InvalidLotQuantity,
        InventoryAttentionReason.InvalidExpiryDate,
        InventoryAttentionReason.Expired,
        InventoryAttentionReason.ExpiresToday,
        InventoryAttentionReason.SurplusAtExpiry,
        InventoryAttentionReason.NearExpiryWithoutSurplus,
        InventoryAttentionReason.ProjectedExcess30,
        InventoryAttentionReason.Idle,
        InventoryAttentionReason.DatedWithoutSurplusInWindow,
        InventoryAttentionReason.Undated,
        InventoryAttentionReason.NoLot,
        InventoryAttentionReason.InsufficientHistory,
        InventoryAttentionReason.NoPhysicalEvidence,
        InventoryAttentionReason.CompositionProduct,
        InventoryAttentionReason.NoObservableDemand,
    ];

    public static InventoryAttentionResult Evaluate(
        ProductTurnoverRow? turnover,
        InventoryProjectedProduct? product = null)
    {
        product ??= new InventoryProjectedProduct();
        var projection = product.Projection ?? new InventoryProjectionResult();
        var lots = projection.Lots ?? [];
        var productId = turnover?.ProductId ?? product.ProductId;

        var collected = CollectReasons(turnover, projection, lots);
        var primary = SelectPrimary(collected);
        var secondary = SelectSecondary(collected, primary);

        var excessQty = FiniteOrNull(
            projection.CanProjectSku ? projection.ProjectedExcessQuantity : null);
        var expirySurplus = FiniteOrNull(
            InventoryProjectionPresentation.SumExpirySurplusQuantity(lots));
        var nearestDated = NearestDatedAttentionDays(lots, collected);
        var valueQuality = ClassifySurplusValueQuality(lots, product.LotCosts);

        if (primary == InventoryAttentionReason.None)
        {
            return new InventoryAttentionResult
            {
                ProductId = productId,
                Priority = InventoryAttentionPriority.Normal,
                Family = InventoryAttentionFamily.Normal,
                PrimaryReason = InventoryAttentionReason.None,
                SecondaryReasons = secondary,
                Action = InventoryOperatorAction.None,
                Confidence = InventoryAttentionConfidence.Reliable,
                ProjectedExcessQuantity = excessQty,
                ProjectedExpirySurplusQuantity = expirySurplus,
                NearestDatedDaysUntilExpiry = nearestDated,
                SurplusValueQuality = valueQuality,
            };
        }

        return new InventoryAttentionResult
        {
            ProductId = productId,
            Priority = PriorityOf(primary),
            Family = FamilyOf(primary),
            PrimaryReason = primary,
            SecondaryReasons = secondary,
            Action = ActionOf(primary),
            Confidence = ConfidenceOf(
                primary,
                collected,
                projection,
                valueQuality),
            ProjectedExcessQuantity = excessQty,
            ProjectedExpirySurplusQuantity = expirySurplus,
            NearestDatedDaysUntilExpiry = nearestDated,
            SurplusValueQuality = valueQuality,
        };
    }

    public static IReadOnlyList<InventoryAttentionResult> Apply(InventoryProjectionSnapshot? snapshot) =>
        InventoryAttentionComposer.Build(snapshot).Results;

    static List<InventoryAttentionReason> CollectReasons(
        ProductTurnoverRow? turnover,
        InventoryProjectionResult projection,
        IReadOnlyList<InventoryProjectionLotResult> lots)
    {
        var reasons = new List<InventoryAttentionReason>(8);
        var sku = projection.SkuBlockedReason;
        var expiry = projection.ExpiryBlockedReason;

        if (sku == InventorySkuProjectionBlockedReason.InvalidInput
            || expiry == InventoryExpiryProjectionBlockedReason.InvalidInput
            || HasNonFiniteTurnover(turnover)
            || HasNonFiniteExcess(projection))
            Add(reasons, InventoryAttentionReason.InvalidInput);

        if (sku == InventorySkuProjectionBlockedReason.NegativeStock
            || turnover?.CoverageBand == InventoryCoverageBand.Negative
            || IsNegative(turnover?.TotalStock))
            Add(reasons, InventoryAttentionReason.NegativeStock);

        if (sku == InventorySkuProjectionBlockedReason.NegativeLocationStock
            || expiry == InventoryExpiryProjectionBlockedReason.NegativeLocationStock
            || turnover?.HasLocationStockAnomaly == true)
            Add(reasons, InventoryAttentionReason.NegativeLocationStock);

        if (expiry == InventoryExpiryProjectionBlockedReason.NegativeWarehouseStock)
            Add(reasons, InventoryAttentionReason.NegativeWarehouseStock);

        if (sku == InventorySkuProjectionBlockedReason.InconsistentStockTotals
            || expiry == InventoryExpiryProjectionBlockedReason.InconsistentStockTotals)
            Add(reasons, InventoryAttentionReason.InconsistentStockTotals);

        if (expiry == InventoryExpiryProjectionBlockedReason.TrackedQuantityExceedsWarehouse)
            Add(reasons, InventoryAttentionReason.TrackedQuantityExceedsWarehouse);

        if (expiry == InventoryExpiryProjectionBlockedReason.DuplicateLotId)
            Add(reasons, InventoryAttentionReason.DuplicateLotId);

        if (expiry == InventoryExpiryProjectionBlockedReason.InvalidLotQuantity)
            Add(reasons, InventoryAttentionReason.InvalidLotQuantity);

        if (expiry == InventoryExpiryProjectionBlockedReason.InvalidExpiryDate)
            Add(reasons, InventoryAttentionReason.InvalidExpiryDate);

        var hasExpired = false;
        var hasToday = false;
        var hasUndated = false;
        var nearWithoutSurplus = false;
        var windowWithoutSurplus = false;
        foreach (var lot in lots)
        {
            if (lot.Kind == InventoryProjectionLotKind.AlreadyExpired || lot.AlreadyExpired)
                hasExpired = true;
            else if (lot.Kind == InventoryProjectionLotKind.ExpiresToday)
                hasToday = true;
            else if (lot.Kind == InventoryProjectionLotKind.Undated && IsPositive(lot.Quantity))
                hasUndated = true;
            else if (lot.Kind == InventoryProjectionLotKind.Dated && IsWithoutSurplus(lot)
                && lot.DaysUntilExpiry is int days)
            {
                if (days <= NearExpiryDays)
                    nearWithoutSurplus = true;
                else if (days > NearExpiryDays && days <= MonitorExpiryDays)
                    windowWithoutSurplus = true;
            }
        }

        if (hasExpired)
            Add(reasons, InventoryAttentionReason.Expired);
        if (hasToday)
            Add(reasons, InventoryAttentionReason.ExpiresToday);

        var expirySurplus = InventoryProjectionPresentation.SumExpirySurplusQuantity(lots);
        if (expirySurplus is double surplus && InventoryIntelligenceEngine.IsFinite(surplus)
            && surplus > Epsilon)
            Add(reasons, InventoryAttentionReason.SurplusAtExpiry);

        if (nearWithoutSurplus)
            Add(reasons, InventoryAttentionReason.NearExpiryWithoutSurplus);

        var isComposition = turnover?.IsCompositionProduct == true
            || sku == InventorySkuProjectionBlockedReason.CompositionProduct
            || expiry == InventoryExpiryProjectionBlockedReason.CompositionProduct;

        if (!isComposition
            && projection.CanProjectSku
            && projection.ProjectedExcessQuantity is double excess
            && InventoryIntelligenceEngine.IsFinite(excess)
            && excess > Epsilon)
            Add(reasons, InventoryAttentionReason.ProjectedExcess30);

        if (turnover?.IsIdle == true && !isComposition)
            Add(reasons, InventoryAttentionReason.Idle);

        if (windowWithoutSurplus)
            Add(reasons, InventoryAttentionReason.DatedWithoutSurplusInWindow);

        var noEvidence = sku == InventorySkuProjectionBlockedReason.NoPhysicalEvidence
            || expiry == InventoryExpiryProjectionBlockedReason.NoPhysicalEvidence
            || turnover?.HasPhysicalAvailabilityEvidence == false;
        var insufficientHistory = sku == InventorySkuProjectionBlockedReason.InsufficientHistory
            || expiry == InventoryExpiryProjectionBlockedReason.InsufficientHistory
            || (turnover is { IsHistoryInsufficient30: true, HasPhysicalAvailabilityEvidence: true }
                && !noEvidence);
        var cannotConcludeYet = isComposition || noEvidence || insufficientHistory;

        if (!cannotConcludeYet && hasUndated)
            Add(reasons, InventoryAttentionReason.Undated);

        if (!cannotConcludeYet && ShouldFlagNoLot(lots, turnover, projection, expiry))
            Add(reasons, InventoryAttentionReason.NoLot);

        if (insufficientHistory)
            Add(reasons, InventoryAttentionReason.InsufficientHistory);

        if (noEvidence)
            Add(reasons, InventoryAttentionReason.NoPhysicalEvidence);

        if (isComposition)
            Add(reasons, InventoryAttentionReason.CompositionProduct);

        if (sku == InventorySkuProjectionBlockedReason.NoObservableDemand
            || expiry == InventoryExpiryProjectionBlockedReason.NoObservableDemand)
            Add(reasons, InventoryAttentionReason.NoObservableDemand);

        return reasons;
    }

    static bool ShouldFlagNoLot(
        IReadOnlyList<InventoryProjectionLotResult> lots,
        ProductTurnoverRow? turnover,
        InventoryProjectionResult projection,
        InventoryExpiryProjectionBlockedReason expiry)
    {
        if (lots.Count > 0)
            return false;
        if (expiry is InventoryExpiryProjectionBlockedReason.DuplicateLotId
            or InventoryExpiryProjectionBlockedReason.InvalidExpiryDate)
            return false;
        if (projection.TrackedLotQuantity > Epsilon)
            return false;

        var warehouse = turnover?.Stock ?? 0;
        var untracked = projection.UntrackedWarehouseQuantity;
        return IsPositive(warehouse) || IsPositive(untracked);
    }

    static InventoryAttentionReason SelectPrimary(List<InventoryAttentionReason> collected)
    {
        foreach (var reason in ReasonPrecedence)
        {
            if (collected.Contains(reason))
                return reason;
        }

        return InventoryAttentionReason.None;
    }

    static IReadOnlyList<InventoryAttentionReason> SelectSecondary(
        List<InventoryAttentionReason> collected,
        InventoryAttentionReason primary)
    {
        if (collected.Count == 0)
            return [];

        var secondary = new List<InventoryAttentionReason>(collected.Count);
        foreach (var reason in ReasonPrecedence)
        {
            if (reason == primary)
                continue;
            if (collected.Contains(reason))
                secondary.Add(reason);
        }

        return secondary;
    }

    static InventoryAttentionPriority PriorityOf(InventoryAttentionReason reason) =>
        reason switch
        {
            InventoryAttentionReason.InvalidInput
                or InventoryAttentionReason.NegativeStock
                or InventoryAttentionReason.NegativeLocationStock
                or InventoryAttentionReason.NegativeWarehouseStock
                or InventoryAttentionReason.InconsistentStockTotals
                or InventoryAttentionReason.TrackedQuantityExceedsWarehouse
                or InventoryAttentionReason.DuplicateLotId
                or InventoryAttentionReason.InvalidLotQuantity
                or InventoryAttentionReason.InvalidExpiryDate
                or InventoryAttentionReason.Expired
                => InventoryAttentionPriority.Critical,
            InventoryAttentionReason.ExpiresToday
                or InventoryAttentionReason.SurplusAtExpiry
                => InventoryAttentionPriority.High,
            InventoryAttentionReason.NearExpiryWithoutSurplus
                or InventoryAttentionReason.ProjectedExcess30
                or InventoryAttentionReason.Idle
                => InventoryAttentionPriority.Medium,
            InventoryAttentionReason.DatedWithoutSurplusInWindow
                or InventoryAttentionReason.Undated
                or InventoryAttentionReason.NoLot
                => InventoryAttentionPriority.Low,
            _ => InventoryAttentionPriority.Normal,
        };

    static InventoryAttentionFamily FamilyOf(InventoryAttentionReason reason) =>
        reason switch
        {
            InventoryAttentionReason.InvalidInput
                or InventoryAttentionReason.NegativeStock
                or InventoryAttentionReason.NegativeLocationStock
                or InventoryAttentionReason.NegativeWarehouseStock
                or InventoryAttentionReason.InconsistentStockTotals
                or InventoryAttentionReason.TrackedQuantityExceedsWarehouse
                or InventoryAttentionReason.DuplicateLotId
                or InventoryAttentionReason.InvalidLotQuantity
                or InventoryAttentionReason.InvalidExpiryDate
                or InventoryAttentionReason.Undated
                or InventoryAttentionReason.NoLot
                => InventoryAttentionFamily.DataQuality,
            InventoryAttentionReason.Expired
                or InventoryAttentionReason.ExpiresToday
                or InventoryAttentionReason.SurplusAtExpiry
                or InventoryAttentionReason.NearExpiryWithoutSurplus
                or InventoryAttentionReason.DatedWithoutSurplusInWindow
                => InventoryAttentionFamily.Expiry,
            InventoryAttentionReason.ProjectedExcess30 => InventoryAttentionFamily.Excess,
            InventoryAttentionReason.Idle => InventoryAttentionFamily.Turnover,
            _ => InventoryAttentionFamily.Normal,
        };

    static InventoryOperatorAction ActionOf(InventoryAttentionReason reason) =>
        reason switch
        {
            InventoryAttentionReason.Expired => InventoryOperatorAction.RemoveExpired,
            InventoryAttentionReason.InvalidInput
                or InventoryAttentionReason.NegativeStock
                or InventoryAttentionReason.NegativeLocationStock
                or InventoryAttentionReason.NegativeWarehouseStock
                or InventoryAttentionReason.InconsistentStockTotals
                or InventoryAttentionReason.TrackedQuantityExceedsWarehouse
                or InventoryAttentionReason.DuplicateLotId
                or InventoryAttentionReason.InvalidLotQuantity
                or InventoryAttentionReason.InvalidExpiryDate
                or InventoryAttentionReason.Undated
                or InventoryAttentionReason.NoLot
                => InventoryOperatorAction.ReviewData,
            InventoryAttentionReason.ExpiresToday
                or InventoryAttentionReason.SurplusAtExpiry
                or InventoryAttentionReason.NearExpiryWithoutSurplus
                => InventoryOperatorAction.PrioritizeSale,
            InventoryAttentionReason.DatedWithoutSurplusInWindow
                => InventoryOperatorAction.Monitor,
            InventoryAttentionReason.ProjectedExcess30
                or InventoryAttentionReason.Idle
                => InventoryOperatorAction.EvaluateExcess,
            _ => InventoryOperatorAction.None,
        };

    static InventoryAttentionConfidence ConfidenceOf(
        InventoryAttentionReason primary,
        List<InventoryAttentionReason> collected,
        InventoryProjectionResult projection,
        InventoryProjectionSurplusValueQuality valueQuality)
    {
        if (IsStructural(primary))
            return InventoryAttentionConfidence.Unavailable;

        if (primary is InventoryAttentionReason.InsufficientHistory
            or InventoryAttentionReason.NoPhysicalEvidence
            or InventoryAttentionReason.CompositionProduct
            or InventoryAttentionReason.InvalidInput)
            return InventoryAttentionConfidence.Unavailable;

        if (primary == InventoryAttentionReason.NoObservableDemand)
            return InventoryAttentionConfidence.Unavailable;

        if (collected.Contains(InventoryAttentionReason.InsufficientHistory)
            || collected.Contains(InventoryAttentionReason.NoPhysicalEvidence)
            || collected.Contains(InventoryAttentionReason.CompositionProduct)
            || projection.HasLotLocationLimitation
            || (IsPositive(projection.UntrackedWarehouseQuantity)
                && primary is not InventoryAttentionReason.NoLot
                    and not InventoryAttentionReason.Undated)
            || (collected.Contains(InventoryAttentionReason.SurplusAtExpiry)
                && valueQuality is InventoryProjectionSurplusValueQuality.CompleteWithEstimate
                    or InventoryProjectionSurplusValueQuality.Partial
                    or InventoryProjectionSurplusValueQuality.Unavailable))
            return InventoryAttentionConfidence.Limited;

        return InventoryAttentionConfidence.Reliable;
    }

    static bool IsStructural(InventoryAttentionReason reason) =>
        reason is InventoryAttentionReason.InvalidInput
            or InventoryAttentionReason.NegativeStock
            or InventoryAttentionReason.NegativeLocationStock
            or InventoryAttentionReason.NegativeWarehouseStock
            or InventoryAttentionReason.InconsistentStockTotals
            or InventoryAttentionReason.TrackedQuantityExceedsWarehouse
            or InventoryAttentionReason.DuplicateLotId
            or InventoryAttentionReason.InvalidLotQuantity
            or InventoryAttentionReason.InvalidExpiryDate;

    static InventoryProjectionSurplusValueQuality ClassifySurplusValueQuality(
        IReadOnlyList<InventoryProjectionLotResult> lots,
        IReadOnlyList<InventoryProjectedLotCost>? costs)
    {
        var byLot = new Dictionary<int, InventoryProjectedLotCost>();
        foreach (var cost in costs ?? [])
            byLot.TryAdd(cost.LotId, cost);

        var surplusLots = 0;
        var valued = 0;
        var missing = 0;
        var estimates = 0;
        foreach (var lot in lots)
        {
            if (lot.ProjectedSurplusAtExpiry is not double qty
                || !InventoryIntelligenceEngine.IsFinite(qty)
                || qty <= Epsilon)
                continue;

            surplusLots++;
            byLot.TryGetValue(lot.LotId, out var cost);
            var source = cost?.CostSource ?? LotCostSource.Unavailable;
            if (source == LotCostSource.Unavailable
                || lot.ProjectedSurplusValue is not double value
                || !InventoryIntelligenceEngine.IsFinite(value)
                || value < 0)
            {
                missing++;
                continue;
            }

            valued++;
            if (source == LotCostSource.CurrentAverageEstimate)
                estimates++;
        }

        if (surplusLots == 0)
            return InventoryProjectionSurplusValueQuality.Unavailable;
        if (valued == 0)
            return InventoryProjectionSurplusValueQuality.Unavailable;
        if (missing > 0)
            return InventoryProjectionSurplusValueQuality.Partial;
        if (estimates > 0)
            return InventoryProjectionSurplusValueQuality.CompleteWithEstimate;
        return InventoryProjectionSurplusValueQuality.CompleteRecorded;
    }

    static int? NearestDatedAttentionDays(
        IReadOnlyList<InventoryProjectionLotResult> lots,
        List<InventoryAttentionReason> collected)
    {
        if (!collected.Contains(InventoryAttentionReason.NearExpiryWithoutSurplus)
            && !collected.Contains(InventoryAttentionReason.DatedWithoutSurplusInWindow)
            && !collected.Contains(InventoryAttentionReason.SurplusAtExpiry)
            && !collected.Contains(InventoryAttentionReason.ExpiresToday))
            return null;

        int? nearest = null;
        foreach (var lot in lots)
        {
            if (lot.Kind is not InventoryProjectionLotKind.Dated
                and not InventoryProjectionLotKind.ExpiresToday)
                continue;
            if (lot.DaysUntilExpiry is not int days)
                continue;
            if (nearest is null || days < nearest.Value)
                nearest = days;
        }

        return nearest;
    }

    static void Add(List<InventoryAttentionReason> reasons, InventoryAttentionReason reason)
    {
        if (!reasons.Contains(reason))
            reasons.Add(reason);
    }

    static bool HasNonFiniteTurnover(ProductTurnoverRow? turnover)
    {
        if (turnover is null)
            return false;
        return !InventoryIntelligenceEngine.IsFinite(turnover.Stock)
            || !InventoryIntelligenceEngine.IsFinite(turnover.StockFridge)
            || !InventoryIntelligenceEngine.IsFinite(turnover.TotalStock)
            || !InventoryIntelligenceEngine.IsFinite(turnover.Vmv7)
            || !InventoryIntelligenceEngine.IsFinite(turnover.Vmv30)
            || !InventoryIntelligenceEngine.IsFinite(turnover.Vmv90);
    }

    static bool HasNonFiniteExcess(InventoryProjectionResult projection) =>
        projection.CanProjectSku
        && projection.ProjectedExcessQuantity is double qty
        && !InventoryIntelligenceEngine.IsFinite(qty);

    static bool IsWithoutSurplus(InventoryProjectionLotResult lot) =>
        lot.ProjectedSurplusAtExpiry is not double surplus
        || !InventoryIntelligenceEngine.IsFinite(surplus)
        || surplus <= Epsilon;

    static bool IsPositive(double? value) =>
        value is double number
        && InventoryIntelligenceEngine.IsFinite(number)
        && number > Epsilon;

    static bool IsNegative(double? value) =>
        value is double number
        && InventoryIntelligenceEngine.IsFinite(number)
        && number < -Epsilon;

    static double? FiniteOrNull(double? value) =>
        value is double number && InventoryIntelligenceEngine.IsFinite(number)
            ? number
            : null;
}
