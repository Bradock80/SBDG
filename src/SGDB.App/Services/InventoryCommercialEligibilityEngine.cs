using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Motor puro 70F-B1: elegibilidade comercial a partir de 70C/70D/70E já calculados.
/// Sem I/O, UI, preço, desconto ou promoção. Sem score.
/// Não recalcula VMV, cobertura, FEFO, IsIdle, Priority nem Action.
/// Não sobrescreve <see cref="InventoryOperatorAction"/>. EvaluateExcess não é promoção.
/// QueryCount = 0.
/// </summary>
public static class InventoryCommercialEligibilityEngine
{
    public const double Epsilon = InventoryIntelligenceEngine.Epsilon;
    public const int ExpectedQueryCount = 0;

    /// <summary>
    /// Precedência 70F (pergunta diferente da 70E): vencido e dados inválidos
    /// antes de monitoramento; tese comercial só depois da segurança.
    /// </summary>
    public static readonly InventoryCommercialEligibilityReason[] ReasonPrecedence =
    [
        InventoryCommercialEligibilityReason.Expired,
        InventoryCommercialEligibilityReason.InvalidInput,
        InventoryCommercialEligibilityReason.NegativeStock,
        InventoryCommercialEligibilityReason.NegativeLocationStock,
        InventoryCommercialEligibilityReason.NegativeWarehouseStock,
        InventoryCommercialEligibilityReason.InconsistentStockTotals,
        InventoryCommercialEligibilityReason.TrackedQuantityExceedsWarehouse,
        InventoryCommercialEligibilityReason.DuplicateLotId,
        InventoryCommercialEligibilityReason.InvalidLotQuantity,
        InventoryCommercialEligibilityReason.InvalidExpiryDate,
        InventoryCommercialEligibilityReason.ProjectionMissing,
        InventoryCommercialEligibilityReason.DuplicateProjection,
        InventoryCommercialEligibilityReason.CompositionProduct,
        InventoryCommercialEligibilityReason.InsufficientHistory,
        InventoryCommercialEligibilityReason.NoPhysicalEvidence,
        InventoryCommercialEligibilityReason.LocationLimitation,
        InventoryCommercialEligibilityReason.Undated,
        InventoryCommercialEligibilityReason.NoLot,
        InventoryCommercialEligibilityReason.AnalysisUnavailable,
        InventoryCommercialEligibilityReason.ExpiresToday,
        InventoryCommercialEligibilityReason.NearExpiryWithoutSurplus,
        InventoryCommercialEligibilityReason.DatedWithoutSurplusInWindow,
        InventoryCommercialEligibilityReason.ProjectedExpirySurplus,
        InventoryCommercialEligibilityReason.ProjectedExcess,
        InventoryCommercialEligibilityReason.Idle,
        InventoryCommercialEligibilityReason.HighCoverageWithoutExcess,
        InventoryCommercialEligibilityReason.NoObservableDemand,
        InventoryCommercialEligibilityReason.ZeroStock,
    ];

    public static InventoryCommercialEligibilityResult Evaluate(
        ProductTurnoverRow? turnover,
        InventoryProjectedProduct? projected = null,
        InventoryAttentionResult? attention = null)
    {
        projected ??= new InventoryProjectedProduct();
        var projection = projected.Projection ?? new InventoryProjectionResult();
        var lots = projection.Lots ?? [];
        var productId = turnover?.ProductId ?? projected.ProductId;
        if (attention is { ProductId: > 0 } && productId == 0)
            productId = attention.ProductId;

        var collected = CollectReasons(turnover, projection, lots, attention);
        var primary = SelectPrimary(collected);
        var secondary = SelectSecondary(collected, primary);
        var confidence = attention?.Confidence ?? InventoryAttentionConfidence.Unavailable;
        var kind = KindOf(primary);

        if (kind == InventoryCommercialEligibilityKind.CommercialCandidate
            && !MayBeCommercialCandidate(turnover, confidence, collected))
        {
            kind = InventoryCommercialEligibilityKind.NoCommercialRecommendation;
            if (primary is InventoryCommercialEligibilityReason.ProjectedExcess
                or InventoryCommercialEligibilityReason.ProjectedExpirySurplus
                or InventoryCommercialEligibilityReason.Idle)
            {
                primary = InventoryCommercialEligibilityReason.AnalysisUnavailable;
                secondary = SelectSecondary(collected, primary);
            }
        }

        return new InventoryCommercialEligibilityResult
        {
            ProductId = productId,
            Kind = kind,
            PrimaryReason = primary,
            SecondaryReasons = secondary,
            Confidence = confidence,
        };
    }

    static List<InventoryCommercialEligibilityReason> CollectReasons(
        ProductTurnoverRow? turnover,
        InventoryProjectionResult projection,
        IReadOnlyList<InventoryProjectionLotResult> lots,
        InventoryAttentionResult? attention)
    {
        var reasons = new List<InventoryCommercialEligibilityReason>(8);
        if (turnover is null || attention is null)
            Add(reasons, InventoryCommercialEligibilityReason.InvalidInput);

        foreach (var mapped in EnumerateMappedAttention(attention))
            Add(reasons, mapped);

        if (turnover?.IsCompositionProduct == true
            || projection.SkuBlockedReason == InventorySkuProjectionBlockedReason.CompositionProduct
            || projection.ExpiryBlockedReason == InventoryExpiryProjectionBlockedReason.CompositionProduct)
            Add(reasons, InventoryCommercialEligibilityReason.CompositionProduct);

        if (IsNegative(turnover?.TotalStock) || turnover?.CoverageBand == InventoryCoverageBand.Negative)
            Add(reasons, InventoryCommercialEligibilityReason.NegativeStock);

        if (turnover?.HasLocationStockAnomaly == true)
            Add(reasons, InventoryCommercialEligibilityReason.NegativeLocationStock);

        if (HasExpiredLot(lots) || HasAttention(attention, InventoryAttentionReason.Expired))
            Add(reasons, InventoryCommercialEligibilityReason.Expired);

        if (HasExpiresTodayLot(lots) || HasAttention(attention, InventoryAttentionReason.ExpiresToday))
            Add(reasons, InventoryCommercialEligibilityReason.ExpiresToday);

        if (HasExcessThesis(attention, projection))
            Add(reasons, InventoryCommercialEligibilityReason.ProjectedExcess);

        if (HasSurplusThesis(attention))
            Add(reasons, InventoryCommercialEligibilityReason.ProjectedExpirySurplus);

        if (turnover?.IsIdle == true && turnover.IsCompositionProduct == false)
            Add(reasons, InventoryCommercialEligibilityReason.Idle);

        if (projection.HasLotLocationLimitation)
            Add(reasons, InventoryCommercialEligibilityReason.LocationLimitation);

        if (turnover is { HasPhysicalAvailabilityEvidence: false })
            Add(reasons, InventoryCommercialEligibilityReason.NoPhysicalEvidence);

        if (turnover is { IsHistoryInsufficient30: true, HasPhysicalAvailabilityEvidence: true })
            Add(reasons, InventoryCommercialEligibilityReason.InsufficientHistory);

        var stock = turnover?.TotalStock;
        if (stock is double qty && InventoryIntelligenceEngine.IsFinite(qty)
            && qty > -Epsilon && qty <= Epsilon)
            Add(reasons, InventoryCommercialEligibilityReason.ZeroStock);

        if (attention?.Confidence == InventoryAttentionConfidence.Unavailable
            && !reasons.Exists(IsBlockingOrUnavailableCarrier))
            Add(reasons, InventoryCommercialEligibilityReason.AnalysisUnavailable);

        var hasThesis = reasons.Contains(InventoryCommercialEligibilityReason.ProjectedExcess)
            || reasons.Contains(InventoryCommercialEligibilityReason.ProjectedExpirySurplus)
            || reasons.Contains(InventoryCommercialEligibilityReason.Idle);
        if (!hasThesis
            && turnover?.CoverageBand == InventoryCoverageBand.Normal
            && IsPositive(turnover.TotalStock))
            Add(reasons, InventoryCommercialEligibilityReason.HighCoverageWithoutExcess);

        return reasons;
    }

    static IEnumerable<InventoryCommercialEligibilityReason> EnumerateMappedAttention(
        InventoryAttentionResult? attention)
    {
        if (attention is null)
            yield break;

        if (Map(attention.PrimaryReason) is { } primary)
            yield return primary;

        foreach (var reason in attention.SecondaryReasons ?? [])
        {
            if (Map(reason) is { } mapped)
                yield return mapped;
        }
    }

    static InventoryCommercialEligibilityReason? Map(InventoryAttentionReason reason) =>
        reason switch
        {
            InventoryAttentionReason.Expired => InventoryCommercialEligibilityReason.Expired,
            InventoryAttentionReason.InvalidInput => InventoryCommercialEligibilityReason.InvalidInput,
            InventoryAttentionReason.NegativeStock => InventoryCommercialEligibilityReason.NegativeStock,
            InventoryAttentionReason.NegativeLocationStock =>
                InventoryCommercialEligibilityReason.NegativeLocationStock,
            InventoryAttentionReason.NegativeWarehouseStock =>
                InventoryCommercialEligibilityReason.NegativeWarehouseStock,
            InventoryAttentionReason.InconsistentStockTotals =>
                InventoryCommercialEligibilityReason.InconsistentStockTotals,
            InventoryAttentionReason.TrackedQuantityExceedsWarehouse =>
                InventoryCommercialEligibilityReason.TrackedQuantityExceedsWarehouse,
            InventoryAttentionReason.DuplicateLotId => InventoryCommercialEligibilityReason.DuplicateLotId,
            InventoryAttentionReason.InvalidLotQuantity =>
                InventoryCommercialEligibilityReason.InvalidLotQuantity,
            InventoryAttentionReason.InvalidExpiryDate => InventoryCommercialEligibilityReason.InvalidExpiryDate,
            InventoryAttentionReason.ProjectionMissing => InventoryCommercialEligibilityReason.ProjectionMissing,
            InventoryAttentionReason.DuplicateProjection =>
                InventoryCommercialEligibilityReason.DuplicateProjection,
            InventoryAttentionReason.CompositionProduct =>
                InventoryCommercialEligibilityReason.CompositionProduct,
            InventoryAttentionReason.InsufficientHistory =>
                InventoryCommercialEligibilityReason.InsufficientHistory,
            InventoryAttentionReason.NoPhysicalEvidence =>
                InventoryCommercialEligibilityReason.NoPhysicalEvidence,
            InventoryAttentionReason.Undated => InventoryCommercialEligibilityReason.Undated,
            InventoryAttentionReason.NoLot => InventoryCommercialEligibilityReason.NoLot,
            InventoryAttentionReason.ExpiresToday => InventoryCommercialEligibilityReason.ExpiresToday,
            InventoryAttentionReason.NearExpiryWithoutSurplus =>
                InventoryCommercialEligibilityReason.NearExpiryWithoutSurplus,
            InventoryAttentionReason.DatedWithoutSurplusInWindow =>
                InventoryCommercialEligibilityReason.DatedWithoutSurplusInWindow,
            InventoryAttentionReason.SurplusAtExpiry =>
                InventoryCommercialEligibilityReason.ProjectedExpirySurplus,
            InventoryAttentionReason.ProjectedExcess30 =>
                InventoryCommercialEligibilityReason.ProjectedExcess,
            InventoryAttentionReason.Idle => InventoryCommercialEligibilityReason.Idle,
            InventoryAttentionReason.NoObservableDemand =>
                InventoryCommercialEligibilityReason.NoObservableDemand,
            _ => null,
        };

    static bool MayBeCommercialCandidate(
        ProductTurnoverRow? turnover,
        InventoryAttentionConfidence confidence,
        List<InventoryCommercialEligibilityReason> collected)
    {
        if (confidence == InventoryAttentionConfidence.Unavailable)
            return false;
        if (turnover is null || !IsPositive(turnover.TotalStock))
            return false;
        if (turnover.IsCompositionProduct)
            return false;
        if (collected.Contains(InventoryCommercialEligibilityReason.Expired))
            return false;
        if (collected.Contains(InventoryCommercialEligibilityReason.ExpiresToday))
            return false;
        if (collected.Exists(IsStructuralBlock))
            return false;
        return collected.Contains(InventoryCommercialEligibilityReason.ProjectedExcess)
            || collected.Contains(InventoryCommercialEligibilityReason.ProjectedExpirySurplus)
            || collected.Contains(InventoryCommercialEligibilityReason.Idle);
    }

    static InventoryCommercialEligibilityKind KindOf(InventoryCommercialEligibilityReason reason) =>
        reason switch
        {
            InventoryCommercialEligibilityReason.ProjectedExpirySurplus
                or InventoryCommercialEligibilityReason.ProjectedExcess
                or InventoryCommercialEligibilityReason.Idle
                => InventoryCommercialEligibilityKind.CommercialCandidate,
            InventoryCommercialEligibilityReason.ExpiresToday
                or InventoryCommercialEligibilityReason.NearExpiryWithoutSurplus
                or InventoryCommercialEligibilityReason.DatedWithoutSurplusInWindow
                or InventoryCommercialEligibilityReason.HighCoverageWithoutExcess
                => InventoryCommercialEligibilityKind.MonitorOnly,
            InventoryCommercialEligibilityReason.InvalidInput
                or InventoryCommercialEligibilityReason.NegativeStock
                or InventoryCommercialEligibilityReason.NegativeLocationStock
                or InventoryCommercialEligibilityReason.NegativeWarehouseStock
                or InventoryCommercialEligibilityReason.InconsistentStockTotals
                or InventoryCommercialEligibilityReason.TrackedQuantityExceedsWarehouse
                or InventoryCommercialEligibilityReason.DuplicateLotId
                or InventoryCommercialEligibilityReason.InvalidLotQuantity
                or InventoryCommercialEligibilityReason.InvalidExpiryDate
                or InventoryCommercialEligibilityReason.ProjectionMissing
                or InventoryCommercialEligibilityReason.DuplicateProjection
                or InventoryCommercialEligibilityReason.InsufficientHistory
                or InventoryCommercialEligibilityReason.NoPhysicalEvidence
                or InventoryCommercialEligibilityReason.LocationLimitation
                or InventoryCommercialEligibilityReason.Undated
                or InventoryCommercialEligibilityReason.NoLot
                => InventoryCommercialEligibilityKind.ReviewData,
            _ => InventoryCommercialEligibilityKind.NoCommercialRecommendation,
        };

    static InventoryCommercialEligibilityReason SelectPrimary(
        List<InventoryCommercialEligibilityReason> collected)
    {
        foreach (var reason in ReasonPrecedence)
        {
            if (collected.Contains(reason))
                return reason;
        }

        return InventoryCommercialEligibilityReason.None;
    }

    static IReadOnlyList<InventoryCommercialEligibilityReason> SelectSecondary(
        List<InventoryCommercialEligibilityReason> collected,
        InventoryCommercialEligibilityReason primary)
    {
        if (collected.Count == 0)
            return [];

        var secondary = new List<InventoryCommercialEligibilityReason>(collected.Count);
        foreach (var reason in ReasonPrecedence)
        {
            if (reason == primary)
                continue;
            if (collected.Contains(reason))
                secondary.Add(reason);
        }

        return secondary;
    }

    static bool HasExcessThesis(InventoryAttentionResult? attention, InventoryProjectionResult projection)
    {
        if (QuantityThesis(attention?.ProjectedExcessQuantity))
            return true;
        if (attention is not null)
            return false;
        return projection.CanProjectSku && QuantityThesis(projection.ProjectedExcessQuantity);
    }

    static bool HasSurplusThesis(InventoryAttentionResult? attention) =>
        QuantityThesis(attention?.ProjectedExpirySurplusQuantity);

    static bool QuantityThesis(double? quantity) =>
        quantity is double value
        && InventoryIntelligenceEngine.IsFinite(value)
        && value > Epsilon;

    static bool HasExpiredLot(IReadOnlyList<InventoryProjectionLotResult> lots)
    {
        foreach (var lot in lots)
        {
            if (lot.Kind == InventoryProjectionLotKind.AlreadyExpired || lot.AlreadyExpired)
                return true;
        }

        return false;
    }

    static bool HasExpiresTodayLot(IReadOnlyList<InventoryProjectionLotResult> lots)
    {
        foreach (var lot in lots)
        {
            if (lot.Kind == InventoryProjectionLotKind.ExpiresToday)
                return true;
        }

        return false;
    }

    static bool HasAttention(InventoryAttentionResult? attention, InventoryAttentionReason reason)
    {
        if (attention is null)
            return false;
        if (attention.PrimaryReason == reason)
            return true;
        foreach (var item in attention.SecondaryReasons ?? [])
        {
            if (item == reason)
                return true;
        }

        return false;
    }

    static bool IsStructuralBlock(InventoryCommercialEligibilityReason reason) =>
        reason is InventoryCommercialEligibilityReason.InvalidInput
            or InventoryCommercialEligibilityReason.NegativeStock
            or InventoryCommercialEligibilityReason.NegativeLocationStock
            or InventoryCommercialEligibilityReason.NegativeWarehouseStock
            or InventoryCommercialEligibilityReason.InconsistentStockTotals
            or InventoryCommercialEligibilityReason.TrackedQuantityExceedsWarehouse
            or InventoryCommercialEligibilityReason.DuplicateLotId
            or InventoryCommercialEligibilityReason.InvalidLotQuantity
            or InventoryCommercialEligibilityReason.InvalidExpiryDate
            or InventoryCommercialEligibilityReason.ProjectionMissing
            or InventoryCommercialEligibilityReason.DuplicateProjection
            or InventoryCommercialEligibilityReason.InsufficientHistory
            or InventoryCommercialEligibilityReason.NoPhysicalEvidence
            or InventoryCommercialEligibilityReason.LocationLimitation;

    static bool IsBlockingOrUnavailableCarrier(InventoryCommercialEligibilityReason reason) =>
        IsStructuralBlock(reason)
        || reason is InventoryCommercialEligibilityReason.Expired
            or InventoryCommercialEligibilityReason.CompositionProduct
            or InventoryCommercialEligibilityReason.NoObservableDemand
            or InventoryCommercialEligibilityReason.AnalysisUnavailable;

    static bool IsPositive(double? value) =>
        value is double number
        && InventoryIntelligenceEngine.IsFinite(number)
        && number > Epsilon;

    static bool IsNegative(double? value) =>
        value is double number
        && InventoryIntelligenceEngine.IsFinite(number)
        && number < -Epsilon;

    static void Add(List<InventoryCommercialEligibilityReason> reasons, InventoryCommercialEligibilityReason reason)
    {
        if (reason == InventoryCommercialEligibilityReason.None)
            return;
        if (!reasons.Contains(reason))
            reasons.Add(reason);
    }
}
