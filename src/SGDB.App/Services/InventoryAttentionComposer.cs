using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Composição 70E-B2: aplica o motor B1 sobre o snapshot 70D já carregado.
/// Sem I/O, recálculo 70C/70D, UI ou score. Autoridade da lista: Intelligence.Rows.
/// Lookup por ProductId (O(n)); conflitos 70D iguais à B4A (não last-wins).
/// </summary>
public static class InventoryAttentionComposer
{
    public static InventoryAttentionSnapshot Build(InventoryProjectionSnapshot? snapshot)
    {
        snapshot ??= new InventoryProjectionSnapshot();
        var rows = snapshot.Intelligence?.Rows ?? [];
        var lookup = snapshot.ByProductId ?? new Dictionary<int, InventoryProjectedProduct>();
        return Compose(snapshot.Today, snapshot.QueryCount, rows, lookup, conflicts: null);
    }

    /// <summary>
    /// Indexa a lista 70D como a B4A: primeiro ganha; ProductId repetido vira conflito.
    /// Produtos 70D sem linha 70C não entram no resultado.
    /// </summary>
    public static InventoryAttentionSnapshot Build(
        DateTime today,
        int queryCount,
        IReadOnlyList<ProductTurnoverRow>? rows,
        IReadOnlyList<InventoryProjectedProduct>? projections)
    {
        var lookup = IndexProjections(projections, out var conflicts);
        return Compose(today, queryCount, rows ?? [], lookup, conflicts);
    }

    static InventoryAttentionSnapshot Compose(
        DateTime today,
        int queryCount,
        IReadOnlyList<ProductTurnoverRow> rows,
        IReadOnlyDictionary<int, InventoryProjectedProduct> lookup,
        HashSet<int>? conflicts)
    {
        var results = new List<InventoryAttentionResult>(rows.Count);
        var map = new Dictionary<int, InventoryAttentionResult>(rows.Count);

        foreach (var row in rows)
        {
            var result = ClassifyRow(row, lookup, conflicts);
            results.Add(result);
            map.TryAdd(row.ProductId, result);
        }

        return new InventoryAttentionSnapshot
        {
            Today = today.Date,
            QueryCount = queryCount,
            Results = results,
            ByProductId = map,
        };
    }

    static InventoryAttentionResult ClassifyRow(
        ProductTurnoverRow row,
        IReadOnlyDictionary<int, InventoryProjectedProduct> lookup,
        HashSet<int>? conflicts)
    {
        if (conflicts is not null && conflicts.Contains(row.ProductId))
            return CompositionIssue(row.ProductId, InventoryAttentionReason.DuplicateProjection);

        if (!lookup.TryGetValue(row.ProductId, out var projected) || projected is null)
            return CompositionIssue(row.ProductId, InventoryAttentionReason.ProjectionMissing);

        return InventoryAttentionEngine.Evaluate(row, projected);
    }

    static InventoryAttentionResult CompositionIssue(int productId, InventoryAttentionReason reason) =>
        new()
        {
            ProductId = productId,
            Priority = reason == InventoryAttentionReason.DuplicateProjection
                ? InventoryAttentionPriority.Critical
                : InventoryAttentionPriority.Low,
            Family = InventoryAttentionFamily.DataQuality,
            PrimaryReason = reason,
            SecondaryReasons = [],
            Action = InventoryOperatorAction.ReviewData,
            Confidence = InventoryAttentionConfidence.Unavailable,
            SurplusValueQuality = InventoryProjectionSurplusValueQuality.Unavailable,
        };

    /// <summary>
    /// Mesma política de <c>InventoryIntelligenceProjectionPresentation.IndexProjections</c>:
    /// repetido → remove o primeiro e marca conflito. Não escolhe um valor entre dois.
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
