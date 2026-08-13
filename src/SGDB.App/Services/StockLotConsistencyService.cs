using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Diagnóstico somente leitura: products.stock × SUM(product_lots.quantity).
/// Não altera estoque, lotes, inventário nem movimentos.
/// </summary>
public static class StockLotConsistencyService
{
    public const double Tolerance = StockLotConsistencyRow.Tolerance;

    public static IReadOnlyList<StockLotConsistencyRow> List(StockLotConsistencyQuery? query = null)
    {
        query ??= new StockLotConsistencyQuery();
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                p.id,
                IFNULL(p.code, ''),
                IFNULL(p.name, ''),
                IFNULL(p.stock, 0),
                IFNULL(SUM(l.quantity), 0),
                SUM(CASE WHEN IFNULL(l.quantity, 0) > 0.0001 THEN 1 ELSE 0 END),
                IFNULL(p.extra_json, '')
            FROM products p
            LEFT JOIN product_lots l ON l.product_id = p.id
            WHERE IFNULL(p.active, 1) = 1
            GROUP BY p.id
            ORDER BY p.name COLLATE NOCASE;
            """;

        var rows = new List<StockLotConsistencyRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var productId = reader.GetInt32(0);
            var code = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var name = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var global = reader.GetDouble(3);
            var lots = reader.GetDouble(4);
            var lotCount = reader.GetInt32(5);
            var extraJson = reader.IsDBNull(6) ? "" : reader.GetString(6);
            var expiryControl = ProductExtra.Parse(extraJson).ControleValidade;
            var hasLots = lotCount > 0;
            var difference = Math.Round(global - lots, 4);

            if (query.OnlyWithLotsOrExpiryControl && !hasLots && expiryControl != true)
                continue;

            if (query.OnlyDivergent && Math.Abs(difference) <= Tolerance)
                continue;

            if (search is not null)
            {
                if (!name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    && !code.Contains(search, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            rows.Add(new StockLotConsistencyRow
            {
                ProductId = productId,
                Code = code,
                ProductName = name,
                GlobalStock = global,
                LotsStock = lots,
                Difference = difference,
                HasLots = hasLots,
                ExpiryControl = expiryControl,
            });
        }

        return rows;
    }
}
