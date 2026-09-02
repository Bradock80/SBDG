using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// 70F-B2 — leitura batch de fatos comerciais. Uma query. Sem N+1.
/// Não recalcula 70C/70D/70E. Não usa promoção cadastrada nem último custo como média.
/// </summary>
public static class InventoryCommercialFactsService
{
    public const int ExpectedQueryCount = 1;

    public static InventoryCommercialFactsSnapshot Load(IReadOnlyList<int>? productIds)
    {
        var requested = DistinctIds(productIds);
        if (requested.Count == 0)
        {
            return new InventoryCommercialFactsSnapshot
            {
                QueryCount = 0,
                RequestedProductIds = [],
            };
        }

        var found = LoadRows(requested);
        var rows = new List<InventoryCommercialFacts>(requested.Count);
        var map = new Dictionary<int, InventoryCommercialFacts>(requested.Count);
        foreach (var id in requested)
        {
            found.TryGetValue(id, out var row);
            var facts = InventoryCommercialFactsEngine.Classify(row ?? new InventoryCommercialFactsInput
            {
                ProductId = id,
                ProductFound = false,
            });
            rows.Add(facts);
            map.TryAdd(id, facts);
        }

        return new InventoryCommercialFactsSnapshot
        {
            QueryCount = ExpectedQueryCount,
            RequestedProductIds = requested,
            Rows = rows,
            ByProductId = map,
        };
    }

    static List<int> DistinctIds(IReadOnlyList<int>? productIds)
    {
        if (productIds is null || productIds.Count == 0)
            return [];

        var seen = new HashSet<int>();
        var ordered = new List<int>();
        foreach (var id in productIds)
        {
            if (!seen.Add(id))
                continue;
            ordered.Add(id);
        }

        return ordered;
    }

    static Dictionary<int, InventoryCommercialFactsInput> LoadRows(IReadOnlyList<int> ids)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var names = new string[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            names[i] = "$id" + i;
            cmd.Parameters.AddWithValue(names[i], ids[i]);
        }

        cmd.CommandText = $"""
            SELECT
                p.id,
                IFNULL(p.name, ''),
                IFNULL(p.group_name, ''),
                p.sale_price,
                p.cost_price,
                IFNULL(p.extra_json, '')
            FROM products p
            WHERE p.id IN ({string.Join(", ", names)});
            """;

        var map = new Dictionary<int, InventoryCommercialFactsInput>(ids.Count);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var extra = ProductExtra.Parse(reader.IsDBNull(5) ? "" : reader.GetString(5));
            var name = reader.GetString(1);
            var group = reader.GetString(2);
            var id = reader.GetInt32(0);
            map[id] = new InventoryCommercialFactsInput
            {
                ProductId = id,
                ProductFound = true,
                CatalogSalePrice = ReadRaw(reader, 3),
                CurrentAverageCost = ReadRaw(reader, 4),
                AllowsSale = extra.PermiteVenda,
                IsCompositionProduct = extra.Composicao,
                IsCigaretteProduct = ProductClassificationHelper.IsCigarette(name, group),
                WholesalePrice = extra.PrecoAtacado,
                WholesaleMinimumQuantity = extra.QtdAtacado,
                UnitSalePrice = extra.PrecoAvulso,
            };
        }

        return map;
    }

    static double ReadRaw(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return 0;
        var value = reader.GetValue(ordinal);
        return value switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            long l => l,
            int i => i,
            _ => Convert.ToDouble(value),
        };
    }
}
