using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Composer puro 70G-B2: junta 70C + 70D e chama B1 em memória.
/// Autoridade da população: Intelligence.Rows (70C). Join O(n) por ProductId.
/// Sem I/O, SQL, UI, quantidade, fornecedor, B5 ou recálculo.
/// </summary>
public static class InventoryPurchaseGuidanceComposer
{
    public const int ExpectedQueryCount = 0;

    public static InventoryPurchaseGuidanceSnapshot Compose(
        InventoryProjectionSnapshot? snapshot)
    {
        snapshot ??= new InventoryProjectionSnapshot();
        var rows = snapshot.Intelligence?.Rows ?? [];
        var lookup = snapshot.ByProductId ?? new Dictionary<int, InventoryProjectedProduct>();
        return Compose(rows, lookup, conflicts: null);
    }

    public static InventoryPurchaseGuidanceSnapshot Compose(
        IReadOnlyList<ProductTurnoverRow>? rows,
        IReadOnlyList<InventoryProjectedProduct>? projections)
    {
        var lookup = IndexProjections(projections, out var conflicts);
        return Compose(rows ?? [], lookup, conflicts);
    }

    static InventoryPurchaseGuidanceSnapshot Compose(
        IReadOnlyList<ProductTurnoverRow> rows,
        IReadOnlyDictionary<int, InventoryProjectedProduct> lookup,
        HashSet<int>? conflicts)
    {
        var results = new List<InventoryPurchaseGuidanceResult>(rows.Count);
        var map = new Dictionary<int, InventoryPurchaseGuidanceResult>(rows.Count);

        foreach (var row in rows)
        {
            var result = ClassifyRow(row, lookup, conflicts);
            results.Add(result);
            map.TryAdd(row.ProductId, result);
        }

        return new InventoryPurchaseGuidanceSnapshot
        {
            QueryCount = ExpectedQueryCount,
            Results = results,
            ByProductId = map,
        };
    }

    static InventoryPurchaseGuidanceResult ClassifyRow(
        ProductTurnoverRow row,
        IReadOnlyDictionary<int, InventoryProjectedProduct> lookup,
        HashSet<int>? conflicts)
    {
        if (conflicts is not null && conflicts.Contains(row.ProductId))
            return CompositionIssue(row.ProductId);

        if (!lookup.TryGetValue(row.ProductId, out var projected) || projected is null)
            return CompositionIssue(row.ProductId);

        return InventoryPurchaseGuidanceEngine.Evaluate(MapInput(row, projected));
    }

    static InventoryPurchaseGuidanceInput MapInput(
        ProductTurnoverRow row,
        InventoryProjectedProduct projected)
    {
        var projection = projected.Projection ?? new InventoryProjectionResult();
        var lots = projection.Lots ?? [];

        return new InventoryPurchaseGuidanceInput
        {
            ProductId = row.ProductId,
            Stock = row.Stock,
            StockFridge = row.StockFridge,
            TotalStock = row.TotalStock,
            Vmv30 = row.Vmv30,
            CoverageBand = row.CoverageBand,
            CoverageDays = row.CoverageDays,
            IsIdle = row.IsIdle,
            IsZeroStockWithTurnover = row.IsZeroStockWithTurnover,
            HasPhysicalAvailabilityEvidence = row.HasPhysicalAvailabilityEvidence,
            HistoryDays = row.HistoryDays,
            IsHistoryInsufficient30 = row.IsHistoryInsufficient30,
            IsCompositionProduct = row.IsCompositionProduct,
            HasLocationStockAnomaly = row.HasLocationStockAnomaly,

            CanProjectSku = projection.CanProjectSku,
            ProjectedExcessQuantity = projection.ProjectedExcessQuantity,
            ProjectedExpirySurplus = SumExpirySurplus(lots),
            HasLotLocationLimitation = projection.HasLotLocationLimitation,
            SkuBlockedReason = projection.SkuBlockedReason,
            ExpiryBlockedReason = projection.ExpiryBlockedReason,
            HasExpiredLot = HasLotKind(lots, InventoryProjectionLotKind.AlreadyExpired),
            HasExpiresTodayLot = HasLotKind(lots, InventoryProjectionLotKind.ExpiresToday),
            HasTrackedQuantityExceedsWarehouse =
                projection.ExpiryBlockedReason == InventoryExpiryProjectionBlockedReason.TrackedQuantityExceedsWarehouse,
            HasInvalidLotQuantity =
                projection.ExpiryBlockedReason == InventoryExpiryProjectionBlockedReason.InvalidLotQuantity,
            HasDuplicateLot =
                projection.ExpiryBlockedReason == InventoryExpiryProjectionBlockedReason.DuplicateLotId,
            HasInvalidExpiry =
                projection.ExpiryBlockedReason == InventoryExpiryProjectionBlockedReason.InvalidExpiryDate,
            IsInvalidInput =
                projection.SkuBlockedReason == InventorySkuProjectionBlockedReason.InvalidInput
                || projection.ExpiryBlockedReason == InventoryExpiryProjectionBlockedReason.InvalidInput,
        };
    }

    static double? SumExpirySurplus(IReadOnlyList<InventoryProjectionLotResult> lots) =>
        InventoryProjectionPresentation.SumExpirySurplusQuantity(lots);

    static bool HasLotKind(IReadOnlyList<InventoryProjectionLotResult> lots, InventoryProjectionLotKind kind)
    {
        foreach (var lot in lots)
        {
            if (lot.Kind == kind)
                return true;
            if (kind == InventoryProjectionLotKind.AlreadyExpired && lot.AlreadyExpired)
                return true;
        }

        return false;
    }

    static InventoryPurchaseGuidanceResult CompositionIssue(int productId) =>
        new()
        {
            ProductId = productId,
            Status = InventoryPurchaseGuidanceStatus.ReviewData,
            Action = InventoryPurchaseGuidanceAction.ReviewData,
            Confidence = InventoryAttentionConfidence.Unavailable,
            PrimaryReason = InventoryPurchaseGuidanceReason.StructuralDataIssue,
            SecondaryReasons = [],
        };

    /// <summary>
    /// Mesma política do <c>InventoryAttentionComposer.IndexProjections</c>:
    /// repetido → remove o primeiro e marca conflito. Não escolhe entre dois.
    /// </summary>
    static Dictionary<int, InventoryProjectedProduct> IndexProjections(
        IReadOnlyList<InventoryProjectedProduct>? projections,
        out HashSet<int> conflicts)
    {
        var map = new Dictionary<int, InventoryProjectedProduct>();
        conflicts = [];
        if (projections is null)
            return map;

        foreach (var item in projections)
        {
            if (conflicts.Contains(item.ProductId))
                continue;
            if (map.TryAdd(item.ProductId, item))
                continue;
            map.Remove(item.ProductId);
            conflicts.Add(item.ProductId);
        }

        return map;
    }
}
