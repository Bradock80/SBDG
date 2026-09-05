using Microsoft.Data.Sqlite;
using SGDB.Domain.Commercial;
using SGDB.Domain.Common;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// 71B-B8B — contribuição histórica de lucro bruto por produto.
/// 1 query plana. Rateio por venda em centavos. CMV via HistoricalSaleCostRules.
/// Não lê sale_exchanges. Não explode kit. Sem UI.
/// </summary>
public static class CommercialGoalProductContributionService
{
    public const int ExpectedQueryCount = CommercialGoalProductContributionSnapshot.ExpectedQueryCount;

    public static CommercialGoalProductContributionSnapshot Load(CommercialCompetence competence)
    {
        var fromStr = competence.StartDate.ToString("yyyy-MM-dd");
        var toStr = competence.EndDate.ToString("yyyy-MM-dd");

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
              s.id,
              s.total,
              si.id,
              si.product_id,
              IFNULL(si.product_code, ''),
              IFNULL(si.product_name, ''),
              si.quantity,
              si.unit_price,
              si.subtotal,
              si.cost_at_sale,
              IFNULL(p.cost_price, 0),
              IFNULL(p.extra_json, ''),
              IFNULL(p.group_name, '')
            FROM sales s
            LEFT JOIN sale_items si ON si.sale_id = s.id
            LEFT JOIN products p ON p.id = si.product_id
            WHERE IFNULL(s.cancelled, 0) = 0
              AND s.session_date >= $from AND s.session_date <= $to
            ORDER BY s.id, si.id;
            """;
        cmd.Parameters.AddWithValue("$from", fromStr);
        cmd.Parameters.AddWithValue("$to", toStr);

        var sales = new Dictionary<int, SaleBucket>();
        var saleOrder = new List<int>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var saleId = reader.GetInt32(0);
                if (!sales.TryGetValue(saleId, out var bucket))
                {
                    bucket = new SaleBucket(Money(reader.GetDouble(1)));
                    sales[saleId] = bucket;
                    saleOrder.Add(saleId);
                }

                if (reader.IsDBNull(2))
                    continue;

                var extraJson = reader.IsDBNull(11) ? "" : reader.GetString(11);
                bucket.Lines.Add(new ItemLine(
                    reader.GetInt32(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    reader.IsDBNull(4) ? "" : reader.GetString(4),
                    reader.IsDBNull(5) ? "" : reader.GetString(5),
                    reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
                    reader.IsDBNull(7) ? 0 : reader.GetDouble(7),
                    Money(reader.IsDBNull(8) ? 0 : reader.GetDouble(8)),
                    HistoricalSaleCostRules.ReadCostAtSale(reader, 9),
                    reader.IsDBNull(10) ? 0 : reader.GetDouble(10),
                    extraJson,
                    reader.IsDBNull(12) ? "" : reader.GetString(12),
                    IsComposition(extraJson)));
            }
        }

        return Compose(competence, saleOrder, sales);
    }

    static CommercialGoalProductContributionSnapshot Compose(
        CommercialCompetence competence,
        List<int> saleOrder,
        Dictionary<int, SaleBucket> sales)
    {
        var products = new Dictionary<int, ProductAgg>();
        var usableCmv = new List<SaleLineCmv>();
        var unattributedCents = 0;
        var saleItemCount = 0;
        var unavailableLines = 0;
        var estimatedLines = 0;

        for (var s = 0; s < saleOrder.Count; s++)
        {
            var saleId = saleOrder[s];
            var bucket = sales[saleId];
            if (bucket.Lines.Count == 0)
            {
                unattributedCents += CommercialGoalCents.ToCents(bucket.Total);
                continue;
            }

            var headerLines = new CommercialGoalHeaderAdjustmentLine[bucket.Lines.Count];
            for (var i = 0; i < bucket.Lines.Count; i++)
            {
                var line = bucket.Lines[i];
                headerLines[i] = new CommercialGoalHeaderAdjustmentLine(
                    line.SaleItemId, line.ProductId, line.Subtotal);
            }

            var attributed = CommercialGoalHeaderAdjustmentAllocator.AllocateAttributedCents(
                bucket.Total, headerLines);

            for (var i = 0; i < bucket.Lines.Count; i++)
            {
                var line = bucket.Lines[i];
                saleItemCount++;
                var quality = ResolveLineQuality(line, out var cmv);
                if (quality == CommercialGoalCostQuality.Unavailable)
                    unavailableLines++;
                else if (quality == CommercialGoalCostQuality.EstimatedLegacy)
                    estimatedLines++;

                if (cmv is { } usable)
                    usableCmv.Add(usable);

                if (!products.TryGetValue(line.ProductId, out var agg))
                {
                    agg = new ProductAgg(line.ProductId, line.ProductCode, line.ProductName);
                    products[line.ProductId] = agg;
                }

                agg.AddLine(
                    saleId,
                    attributed[i],
                    line.Quantity,
                    quality,
                    cmv?.TotalCost ?? 0,
                    line.IsComposition);
            }
        }

        var period = HistoricalSaleCostRules.Sum(usableCmv);
        var periodQuality = unavailableLines > 0
            ? CommercialGoalCostQuality.Unavailable
            : estimatedLines > 0
                ? CommercialGoalCostQuality.EstimatedLegacy
                : CommercialGoalCostQuality.Exact;
        var gpAvailable = periodQuality != CommercialGoalCostQuality.Unavailable;

        var cogsCents = CloseCogs(products, period.Total);
        var unattributedRevenue = CommercialGoalCents.FromCents(unattributedCents);
        var revenueCents = unattributedCents;
        foreach (var agg in products.Values)
            revenueCents += agg.RevenueCents;

        var periodCogs = CommercialGoalCents.FromCents(cogsCents);
        var periodRevenue = CommercialGoalCents.FromCents(revenueCents);
        decimal? unattributedGp = gpAvailable ? unattributedRevenue : null;
        decimal? periodGp = gpAvailable
            ? periodRevenue - periodCogs
            : null;

        var shareBase = periodGp;
        var canShare = gpAvailable
            && shareBase is { } gpBase
            && decimal.Abs(gpBase) >= 0.01m;

        var rows = new CommercialGoalProductContributionRow[products.Count];
        var idx = 0;
        foreach (var agg in products.Values)
        {
            var revenue = CommercialGoalCents.FromCents(agg.RevenueCents);
            var cogs = CommercialGoalCents.FromCents(agg.CogsCents);
            var skuUnavailable = agg.Quality == CommercialGoalCostQuality.Unavailable;
            decimal? skuGp = gpAvailable && !skuUnavailable ? revenue - cogs : null;
            decimal? margin = skuGp is { } g && revenue != 0m
                ? MonetaryRounding.RoundDecimal(g / revenue * 100m)
                : null;
            decimal? share = skuGp is { } g2 && canShare
                ? MonetaryRounding.RoundDecimal(g2 / shareBase!.Value)
                : null;

            var rowFlags = CommercialGoalProductContributionLimitation.None;
            if (agg.IsComposition)
                rowFlags |= CommercialGoalProductContributionLimitation.HistoricalBomUnavailable;

            rows[idx++] = new CommercialGoalProductContributionRow
            {
                ProductId = agg.ProductId,
                ProductCode = agg.ProductCode,
                ProductName = agg.ProductName,
                Revenue = revenue,
                Cogs = cogs,
                GrossProfit = skuGp,
                GrossMarginPercent = margin,
                GrossProfitShare = share,
                CostQuality = agg.Quality,
                Limitations = rowFlags,
                UnitsSold = agg.UnitsSold,
                SaleCount = agg.SaleIds.Count,
            };
        }

        Array.Sort(rows, CompareRows);

        var flags = CommercialGoalProductContributionLimitation.ExchangesNotAdjusted;
        if (unattributedCents != 0)
            flags |= CommercialGoalProductContributionLimitation.HasUnattributedRevenue;
        for (var i = 0; i < rows.Length; i++)
        {
            if (rows[i].HasLimitation(CommercialGoalProductContributionLimitation.HistoricalBomUnavailable))
            {
                flags |= CommercialGoalProductContributionLimitation.HistoricalBomUnavailable;
                break;
            }
        }

        return new CommercialGoalProductContributionSnapshot
        {
            Competence = competence,
            Revenue = periodRevenue,
            UnattributedRevenue = unattributedRevenue,
            Cogs = periodCogs,
            UnattributedCogs = 0m,
            GrossProfit = periodGp,
            UnattributedGrossProfit = unattributedGp,
            CostQuality = periodQuality,
            GrossProfitAvailable = gpAvailable,
            SaleCount = saleOrder.Count,
            SaleItemCount = saleItemCount,
            QueryCount = ExpectedQueryCount,
            Limitations = flags,
            Rows = rows,
        };
    }

    static int CloseCogs(Dictionary<int, ProductAgg> products, double periodRawTotal)
    {
        var target = CommercialGoalCents.ToCents(Money(periodRawTotal));
        if (products.Count == 0)
            return target;

        var ids = new int[products.Count];
        var shares = new HamiltonShare[products.Count];
        var n = 0;
        foreach (var kv in products)
        {
            ids[n] = kv.Key;
            var rawCents = kv.Value.UsableCogsRaw == 0
                ? 0
                : CommercialGoalCents.ToCents(Money(kv.Value.UsableCogsRaw));
            var weight = rawCents > 0 ? rawCents : 0;
            shares[n] = new HamiltonShare(weight, kv.Key, 0);
            n++;
        }

        var allocated = HamiltonCentsAllocator.Allocate(target, shares);
        for (var i = 0; i < ids.Length; i++)
            products[ids[i]].CogsCents = allocated[i];

        return target;
    }

    static CommercialGoalCostQuality ResolveLineQuality(ItemLine line, out SaleLineCmv? cmv)
    {
        cmv = null;
        if (!double.IsFinite(line.Quantity) || !double.IsFinite(line.UnitPrice) || !double.IsFinite(line.CatalogCost))
            return CommercialGoalCostQuality.Unavailable;

        var extra = ProductExtra.Parse(line.ExtraJson);
        var resolved = HistoricalSaleCostRules.ResolveLine(
            line.Quantity,
            line.CostAtSale,
            line.CatalogCost,
            line.UnitPrice,
            line.ProductName,
            line.GroupName,
            extra);
        if (!double.IsFinite(resolved.UnitCost) || !double.IsFinite(resolved.TotalCost))
            return CommercialGoalCostQuality.Unavailable;

        cmv = resolved;
        if (resolved.IsHistorical)
            return CommercialGoalCostQuality.Exact;
        return CommercialGoalCostQuality.EstimatedLegacy;
    }

    static bool IsComposition(string extraJson)
    {
        var extra = ProductExtra.Parse(extraJson);
        return extra.Composicao;
    }

    static int CompareRows(
        CommercialGoalProductContributionRow a,
        CommercialGoalProductContributionRow b)
    {
        var aCalc = a.GrossProfit.HasValue;
        var bCalc = b.GrossProfit.HasValue;
        var calcCmp = bCalc.CompareTo(aCalc);
        if (calcCmp != 0)
            return calcCmp;
        if (aCalc && bCalc)
        {
            var gp = b.GrossProfit!.Value.CompareTo(a.GrossProfit!.Value);
            if (gp != 0)
                return gp;
        }

        return a.ProductId.CompareTo(b.ProductId);
    }

    static decimal Money(double value) =>
        Convert.ToDecimal(ProductPriceHelper.RoundPrice(value));

    sealed class SaleBucket
    {
        public SaleBucket(decimal total) => Total = total;
        public decimal Total { get; }
        public List<ItemLine> Lines { get; } = [];
    }

    sealed class ItemLine
    {
        public ItemLine(
            int saleItemId,
            int productId,
            string productCode,
            string productName,
            double quantity,
            double unitPrice,
            decimal subtotal,
            double? costAtSale,
            double catalogCost,
            string extraJson,
            string groupName,
            bool isComposition)
        {
            SaleItemId = saleItemId;
            ProductId = productId;
            ProductCode = productCode;
            ProductName = productName;
            Quantity = quantity;
            UnitPrice = unitPrice;
            Subtotal = subtotal;
            CostAtSale = costAtSale;
            CatalogCost = catalogCost;
            ExtraJson = extraJson;
            GroupName = groupName;
            IsComposition = isComposition;
        }

        public int SaleItemId { get; }
        public int ProductId { get; }
        public string ProductCode { get; }
        public string ProductName { get; }
        public double Quantity { get; }
        public double UnitPrice { get; }
        public decimal Subtotal { get; }
        public double? CostAtSale { get; }
        public double CatalogCost { get; }
        public string ExtraJson { get; }
        public string GroupName { get; }
        public bool IsComposition { get; }
    }

    sealed class ProductAgg
    {
        public ProductAgg(int productId, string code, string name)
        {
            ProductId = productId;
            ProductCode = code;
            ProductName = name;
        }

        public int ProductId { get; }
        public string ProductCode { get; }
        public string ProductName { get; }
        public int RevenueCents;
        public int CogsCents;
        public double UsableCogsRaw;
        public double UnitsSold;
        public CommercialGoalCostQuality Quality = CommercialGoalCostQuality.Exact;
        public bool IsComposition;
        public HashSet<int> SaleIds { get; } = [];

        public void AddLine(
            int saleId,
            int revenueCents,
            double quantity,
            CommercialGoalCostQuality quality,
            double usableCogsRaw,
            bool isComposition)
        {
            RevenueCents += revenueCents;
            SaleIds.Add(saleId);
            if (double.IsFinite(quantity))
                UnitsSold += quantity;
            if (quality != CommercialGoalCostQuality.Unavailable)
                UsableCogsRaw += usableCogsRaw;
            Quality = Worse(Quality, quality);
            if (isComposition)
                IsComposition = true;
        }

        static CommercialGoalCostQuality Worse(
            CommercialGoalCostQuality a,
            CommercialGoalCostQuality b)
        {
            if (a == CommercialGoalCostQuality.Unavailable || b == CommercialGoalCostQuality.Unavailable)
                return CommercialGoalCostQuality.Unavailable;
            if (a == CommercialGoalCostQuality.EstimatedLegacy || b == CommercialGoalCostQuality.EstimatedLegacy)
                return CommercialGoalCostQuality.EstimatedLegacy;
            return CommercialGoalCostQuality.Exact;
        }
    }
}
