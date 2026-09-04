using System.Globalization;
using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// 71A-B2 — coocorrência comercial por transação. Uma query agregada.
/// Fonte: sales + sale_items. Sem movements, BOM, kit explode, N+1 ou ranking.
/// Venda válida = cancelled = 0. O schema não tem outro estado comercial
/// (rascunho/troca/devolução) que invalide o ticket além de cancelled.
/// Janela = 90 dias civis inclusivos em session_date, igual a SumWindow/Window90.
/// HistoryDays vem de 70C; esta camada não recalcula vida do SKU.
/// </summary>
public static class InventoryComboCoOccurrenceService
{
    public const int ExpectedQueryCount = 1;

    public static InventoryComboCoOccurrenceSnapshot Load(
        IReadOnlyList<int>? targetIds,
        IReadOnlyList<int>? anchorIds,
        IReadOnlyDictionary<int, int>? targetHistoryDays = null,
        DateTime? today = null)
    {
        var targets = DistinctIds(targetIds);
        var anchors = DistinctIds(anchorIds);
        if (targets.Count == 0 || anchors.Count == 0)
        {
            return Empty(0, targets, anchors);
        }

        var hasPair = false;
        foreach (var targetId in targets)
        {
            foreach (var anchorId in anchors)
            {
                if (targetId != anchorId)
                {
                    hasPair = true;
                    break;
                }
            }

            if (hasPair)
                break;
        }

        if (!hasPair)
            return Empty(0, targets, anchors);

        var civilToday = (today ?? DateTime.Today).Date;
        var from = civilToday.AddDays(-(InventoryComboPairEvidenceEngine.WindowDays - 1));
        var (targetCounts, pairCounts) = LoadAggregates(targets, anchors, from, civilToday);

        var history = targetHistoryDays ?? new Dictionary<int, int>();
        var rows = new List<InventoryComboPairCoOccurrenceFacts>();
        foreach (var targetId in targets)
        {
            history.TryGetValue(targetId, out var historyDays);
            targetCounts.TryGetValue(targetId, out var targetTx);
            foreach (var anchorId in anchors)
            {
                if (targetId == anchorId)
                    continue;

                pairCounts.TryGetValue((targetId, anchorId), out var pairTx);
                rows.Add(InventoryComboPairEvidenceEngine.Classify(
                    targetId, anchorId, pairTx, targetTx, historyDays));
            }
        }

        return new InventoryComboCoOccurrenceSnapshot
        {
            QueryCount = ExpectedQueryCount,
            RequestedTargetIds = targets,
            RequestedAnchorIds = anchors,
            Rows = rows,
        };
    }

    static InventoryComboCoOccurrenceSnapshot Empty(
        int queryCount,
        IReadOnlyList<int> targets,
        IReadOnlyList<int> anchors) =>
        new()
        {
            QueryCount = queryCount,
            RequestedTargetIds = targets,
            RequestedAnchorIds = anchors,
        };

    static List<int> DistinctIds(IReadOnlyList<int>? ids)
    {
        if (ids is null || ids.Count == 0)
            return [];

        var seen = new HashSet<int>();
        var ordered = new List<int>();
        foreach (var id in ids)
        {
            if (id <= 0 || !seen.Add(id))
                continue;
            ordered.Add(id);
        }

        return ordered;
    }

    static (Dictionary<int, int> TargetCounts, Dictionary<(int, int), int> PairCounts)
        LoadAggregates(
            IReadOnlyList<int> targets,
            IReadOnlyList<int> anchors,
            DateTime from,
            DateTime to)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var targetNames = BindIds(cmd, "t", targets);
        var anchorNames = BindIds(cmd, "a", anchors);

        cmd.CommandText = $"""
            WITH window_sales AS (
                SELECT s.id
                FROM sales s
                WHERE IFNULL(s.cancelled, 0) = 0
                  AND date(s.session_date) >= date($from)
                  AND date(s.session_date) <= date($to)
            ),
            target_sales AS (
                SELECT DISTINCT si.sale_id, si.product_id
                FROM sale_items si
                INNER JOIN window_sales w ON w.id = si.sale_id
                WHERE si.product_id IN ({targetNames})
            ),
            anchor_sales AS (
                SELECT DISTINCT si.sale_id, si.product_id
                FROM sale_items si
                INNER JOIN window_sales w ON w.id = si.sale_id
                WHERE si.product_id IN ({anchorNames})
            )
            SELECT 0 AS row_kind, ts.product_id AS id1, 0 AS id2, COUNT(*) AS n
            FROM target_sales ts
            GROUP BY ts.product_id
            UNION ALL
            SELECT 1, t.product_id, a.product_id, COUNT(*)
            FROM target_sales t
            INNER JOIN anchor_sales a
              ON a.sale_id = t.sale_id AND a.product_id <> t.product_id
            GROUP BY t.product_id, a.product_id;
            """;

        var targetCounts = new Dictionary<int, int>();
        var pairCounts = new Dictionary<(int, int), int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var kind = reader.GetInt32(0);
            var id1 = reader.GetInt32(1);
            var id2 = reader.GetInt32(2);
            var n = reader.GetInt32(3);
            if (kind == 0)
                targetCounts[id1] = n;
            else
                pairCounts[(id1, id2)] = n;
        }

        return (targetCounts, pairCounts);
    }

    static string BindIds(SqliteCommand cmd, string prefix, IReadOnlyList<int> ids)
    {
        var names = new string[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            names[i] = "$" + prefix + i;
            cmd.Parameters.AddWithValue(names[i], ids[i]);
        }

        return string.Join(", ", names);
    }
}
