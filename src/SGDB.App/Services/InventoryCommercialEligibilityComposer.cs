using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// 70F-B4E: elegibilidade B1 em lote sobre snapshots 70C/70D/70E já carregados.
/// Sem I/O. Join O(n) por ProductId. QueryCount = 0.
/// </summary>
public static class InventoryCommercialEligibilityComposer
{
    public const int ExpectedQueryCount = 0;

    public static IReadOnlyList<InventoryCommercialEligibilityResult> Build(
        InventoryProjectionSnapshot? snapshot,
        InventoryAttentionSnapshot? attention)
    {
        var rows = snapshot?.Intelligence.Rows ?? [];
        var projections = snapshot?.ByProductId;
        var attentions = attention?.ByProductId;
        var results = new List<InventoryCommercialEligibilityResult>(rows.Count);
        foreach (var row in rows)
        {
            InventoryProjectedProduct? projected = null;
            projections?.TryGetValue(row.ProductId, out projected);
            InventoryAttentionResult? attentionRow = null;
            attentions?.TryGetValue(row.ProductId, out attentionRow);
            results.Add(InventoryCommercialEligibilityEngine.Evaluate(row, projected, attentionRow));
        }

        return results;
    }

    public static IReadOnlyList<int> ProductIds(InventoryProjectionSnapshot? snapshot)
    {
        var rows = snapshot?.Intelligence.Rows ?? [];
        var ids = new List<int>(rows.Count);
        foreach (var row in rows)
            ids.Add(row.ProductId);
        return ids;
    }
}
