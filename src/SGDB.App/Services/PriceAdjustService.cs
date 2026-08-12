using System.Text.Json;
using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public static class PriceAdjustService
{
    public static double MarginOnSale(double cost, double sale) =>
        ProductPriceCalculator.MarginOnSale(cost, sale);

    /// <summary>
    /// Mantém edges locais: margem ≤0 ou ≥100% devolve o custo arredondado
    /// (Domain <see cref="ProductPriceCalculator.SaleFromCostAndMargin"/> devolve 0 se margem≥100).
    /// </summary>
    public static double SaleFromMargin(double cost, double marginPct)
    {
        var m = marginPct / 100.0;
        if (m >= 1 || m <= 0)
            return ProductPriceCalculator.RoundPrice(cost);
        return ProductPriceCalculator.SaleFromCostAndMargin(cost, marginPct);
    }

    public static IReadOnlyList<PriceAdjustRow> Preview(
        string? search = null,
        string? brand = null,
        string? group = null,
        double? novaMargem = null,
        DateTime? purchaseFrom = null,
        DateTime? purchaseTo = null)
    {
        var products = ProductService.List(search: search, ativo: "ativos", group: group);
        // Filtro por data de compra: produtos que entraram no período (pode combinar com busca).
        var purchaseIds = ProductIdsFromPurchaseRange(purchaseFrom, purchaseTo);

        var brandFilter = (brand ?? "").Trim().ToUpperInvariant();
        if (brandFilter is "" or "TODOS")
            brandFilter = "";

        var nm = novaMargem is > 0 ? novaMargem : null;
        var rows = new List<PriceAdjustRow>();
        foreach (var p in products)
        {
            if (purchaseIds is not null && !purchaseIds.Contains(p.Id))
                continue;
            var row = ToRow(p, nm);
            if (brandFilter.Length > 0 && !string.Equals(row.Brand ?? "", brandFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            rows.Add(row);
        }
        return rows;
    }

    public static int Apply(IEnumerable<(int ProductId, double SalePrice, double? PurchasePrice)> items)
    {
        var updated = 0;
        foreach (var it in items)
        {
            var product = ProductService.GetById(it.ProductId);
            if (product is null) continue;

            var extra = ProductExtra.Parse(product.ExtraJson);
            double costPrice;
            if (it.PurchasePrice is not null)
            {
                extra.PrecoCompra = ProductPriceCalculator.RoundPrice(it.PurchasePrice.Value);
                if (extra.CustosPercent > 0)
                    costPrice = ProductPriceCalculator.CostFromPurchaseAndPercent(extra.PrecoCompra, extra.CustosPercent);
                else
                    costPrice = extra.PrecoCompra;
            }
            else
            {
                costPrice = extra.PrecoCompra > 0 ? extra.PrecoCompra : product.CostPrice;
            }

            var newSale = ProductPriceCalculator.RoundPrice(it.SalePrice);
            if (newSale > 0)
                extra.LucroPercent = MarginOnSale(costPrice, newSale);

            ProductService.Update(it.ProductId, new ProductInput
            {
                Code = product.Code,
                Barcode = product.Barcode,
                Name = product.Name,
                GroupName = product.GroupName,
                Unit = product.Unit,
                CostPrice = it.PurchasePrice is not null ? costPrice : product.CostPrice,
                SalePrice = newSale,
                MinStock = product.MinStock,
                Stock = product.Stock,
                StockFridge = product.StockFridge,
                StockFridgeMin = product.StockFridgeMin,
                Location = product.Location,
                Extra = extra,
                Active = product.Active,
            });
            updated++;
        }
        return updated;
    }

    private static PriceAdjustRow ToRow(Product product, double? novaMargem)
    {
        var extra = ProductExtra.Parse(product.ExtraJson);
        var costPct = ProductPriceCalculator.RoundPrice(extra.CustosPercent);
        var costPrice = ProductPriceCalculator.RoundPrice(product.CostPrice);
        var purchase = ProductPriceCalculator.RoundPrice(extra.PrecoCompra);

        // Preenche Pr.Compra: cadastro → última compra → deriva do custo
        if (purchase <= 0)
            purchase = GetLastPurchaseUnitPrice(product.Id);
        if (purchase <= 0 && costPrice > 0)
        {
            purchase = costPct > 0
                ? ProductPriceCalculator.RoundPrice(costPrice / (1.0 + costPct / 100.0))
                : costPrice;
        }

        if (costPct > 0 && purchase > 0 && costPrice <= 0)
            costPrice = ProductPriceCalculator.CostFromPurchaseAndPercent(purchase, costPct);

        var sale = ProductPriceCalculator.RoundPrice(product.SalePrice);
        var margin = MarginOnSale(costPrice, sale);
        var newMargin = novaMargem is not null ? ProductPriceCalculator.RoundPrice(novaMargem.Value) : margin;
        var newSale = novaMargem is not null ? SaleFromMargin(costPrice, newMargin) : sale;

        var row = new PriceAdjustRow
        {
            ProductId = product.Id,
            Code = product.Code ?? "",
            Barcode = product.Barcode,
            Name = product.Name,
            Brand = string.IsNullOrWhiteSpace(extra.Marca) ? null : extra.Marca.Trim().ToUpperInvariant(),
            CostPercent = costPct,
            MarginPercent = margin,
            SalePrice = sale,
        };
        row.LoadPrices(purchase, costPrice, newMargin, newSale);
        return row;
    }

    /// <summary>Último preço unitário de compra do produto (nota não cancelada).</summary>
    private static double GetLastPurchaseUnitPrice(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT pi.unit_price
            FROM purchase_items pi
            JOIN purchases p ON p.id = pi.purchase_id
            WHERE pi.product_id = $pid
              AND p.status != 'cancelada'
            ORDER BY p.entry_date DESC, p.id DESC, pi.id DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        var val = cmd.ExecuteScalar();
        if (val is null or DBNull)
            return 0;
        return ProductPriceCalculator.RoundPrice(Convert.ToDouble(val));
    }

    private static HashSet<int>? ProductIdsFromPurchaseRange(DateTime? from, DateTime? to)
    {
        if (from is null && to is null)
            return null;

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = """
            SELECT DISTINCT pi.product_id
            FROM purchase_items pi
            JOIN purchases p ON p.id = pi.purchase_id
            WHERE p.status != 'cancelada'
            """;
        if (from is DateTime df)
        {
            sql += " AND p.entry_date >= $from";
            cmd.Parameters.AddWithValue("$from", df.Date.ToString("yyyy-MM-dd"));
        }
        if (to is DateTime dt)
        {
            sql += " AND p.entry_date <= $to";
            cmd.Parameters.AddWithValue("$to", dt.Date.ToString("yyyy-MM-dd"));
        }
        cmd.CommandText = sql;
        var set = new HashSet<int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            set.Add(reader.GetInt32(0));
        return set;
    }
}
