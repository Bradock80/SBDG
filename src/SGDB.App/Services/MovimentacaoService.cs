using System.Globalization;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public static class MovimentacaoService
{
    public static MovimentacaoResult ListProdutos(
        DateTime dateFrom,
        DateTime dateTo,
        string? paymentType = null,
        int limit = 500)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.MovimentacaoProdutos(dateFrom, dateTo, paymentType, limit);
        return ListProdutosLocal(dateFrom, dateTo, paymentType, limit);
    }

    public static MovimentacaoResult ListProdutosLocal(
        DateTime dateFrom,
        DateTime dateTo,
        string? paymentType = null,
        int limit = 500)
    {
        var (dFrom, dTo) = NormalizePeriod(dateFrom, dateTo);
        var lim = Math.Clamp(limit, 1, 2000);
        var feeMap = PaymentMethodsService.FeeInfoByApiLabel();

        using var conn = DatabaseService.OpenConnection();
        var paymentParts = LoadSalePaymentParts(conn, dFrom, dTo);
        var (faturamento, totalVendas) = CalcFaturamento(conn, dFrom, dTo, paymentType, paymentParts);
        var saleFees = LoadSaleFees(conn, dFrom, dTo, feeMap);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT si.sale_id,
                   s.created_at,
                   IFNULL(si.product_code, ''),
                   IFNULL(si.product_name, ''),
                   IFNULL(s.payment_type, ''),
                   IFNULL(p.cost_price, 0),
                   si.quantity,
                   si.unit_price,
                   si.subtotal,
                   si.product_id,
                   IFNULL(p.extra_json, ''),
                   IFNULL(p.group_name, ''),
                   s.total,
                   si.cost_at_sale
            FROM sale_items si
            JOIN sales s ON s.id = si.sale_id
            LEFT JOIN products p ON p.id = si.product_id
            WHERE s.cancelled = 0
              AND s.session_date >= $from
              AND s.session_date <= $to
            ORDER BY s.created_at DESC, si.sale_id DESC, si.id ASC;
            """;
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));

        var work = new List<ProdLineWork>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var saleId = reader.GetInt32(0);
                var payment = reader.IsDBNull(4) ? "" : reader.GetString(4);
                if (!SaleMatchesPayment(saleId, payment, paymentType, paymentParts))
                    continue;

                var qty = reader.GetDouble(6);
                var unitSale = reader.GetDouble(7);
                var subtotal = reader.GetDouble(8);
                var catalogCost = reader.IsDBNull(5) ? 0 : reader.GetDouble(5);
                var productName = reader.IsDBNull(3) ? "" : reader.GetString(3);
                var extraJson = reader.IsDBNull(10) ? "" : reader.GetString(10);
                var extra = ProductExtra.Parse(extraJson);
                var groupName = reader.IsDBNull(11) ? "" : reader.GetString(11);
                var costAtSale = HistoricalSaleCostRules.ReadCostAtSale(reader, 13);
                var lineCmv = HistoricalSaleCostRules.ResolveLine(
                    qty, costAtSale, catalogCost, unitSale, productName, groupName, extra);
                var unitCost = lineCmv.UnitCost;
                var costTotal = ProductPriceHelper.RoundPrice(lineCmv.TotalCost);
                var (discount, acrescimo) = LineDiscountAcrescimo(qty, unitSale, subtotal);
                var saleTotal = reader.GetDouble(12);

                work.Add(new ProdLineWork
                {
                    SaleId = saleId,
                    ProductId = reader.GetInt32(9),
                    Qty = qty,
                    UnitSale = unitSale,
                    LineSubtotal = subtotal,
                    CostTotal = costTotal,
                    ExtraJson = extraJson,
                    SaleTotal = saleTotal,
                    PaymentType = payment,
                    Row = new MovimentacaoProdutoRow
                    {
                        SaleId = saleId,
                        SaleDateBr = FormatBrDateTime(reader.GetString(1)),
                        ProductCode = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        ProductName = productName,
                        PaymentType = payment,
                        UnitCost = unitCost,
                        Qty = ProductPriceHelper.RoundPrice(qty),
                        UnitSale = unitSale,
                        Discount = discount,
                        Acrescimo = acrescimo,
                        Total = subtotal,
                        FeePercent = 0,
                        TaxaValor = 0,
                        TotalLiquido = subtotal,
                        LucroBruto = ProductPriceHelper.RoundPrice(subtotal - costTotal),
                        LucroLiquido = ProductPriceHelper.RoundPrice(subtotal - costTotal),
                    },
                });
            }
        }

        ApplySaleLevelAdjustments(work, paymentParts, saleFees, feeMap);

        var all = work.Select(w => w.Row).ToList();
        var display = all.Take(lim).ToList();
        return BuildResult("produtos", dFrom, dTo, display, [], [], all.Count, faturamento, totalVendas,
            produtosTotais: all);
    }

    private sealed class ProdLineWork
    {
        public int SaleId { get; init; }
        public int ProductId { get; init; }
        public double Qty { get; init; }
        public double UnitSale { get; init; }
        public double LineSubtotal { get; init; }
        public double CostTotal { get; init; }
        public string ExtraJson { get; init; } = "";
        public double SaleTotal { get; init; }
        public string PaymentType { get; init; } = "";
        public required MovimentacaoProdutoRow Row { get; init; }
    }

    /// <summary>
    /// Acréscimo de tabela (ex.: +R$ 1 cigarro no cartão/PIX) grava no total da venda,
    /// não no item. Aqui redistribui para a coluna Acréscimo / Total / Lucro.
    /// Usa a regra da tabela (R$ fixo × quantidade), não só o que foi cobrado na venda
    /// (vendas antigas às vezes cobraram só 1× mesmo com qtd &gt; 1).
    /// </summary>
    private static void ApplySaleLevelAdjustments(
        List<ProdLineWork> work,
        Dictionary<int, List<string>> paymentParts,
        Dictionary<int, (double TaxaTotal, double BaseTotal)> saleFees,
        Dictionary<string, PaymentFeeInfo> feeMap)
    {
        foreach (var group in work.GroupBy(w => w.SaleId))
        {
            var lines = group.ToList();
            var saleTotal = lines[0].SaleTotal;
            var itemsSum = ProductPriceHelper.RoundPrice(lines.Sum(l => l.LineSubtotal));
            var delta = ProductPriceHelper.RoundPrice(saleTotal - itemsSum);

            var parts = paymentParts.GetValueOrDefault(group.Key);
            if (parts is null || parts.Count == 0)
                parts = [lines[0].PaymentType];

            if (delta < -0.02)
            {
                AllocateSaleDelta(lines, -delta, isAcrescimo: false, parts);
            }
            else
            {
                var expectedSum = 0.0;
                for (var i = 0; i < lines.Count; i++)
                {
                    var expected = ExpectedLineSurcharge(lines[i], parts);
                    if (expected > 0.009)
                        lines[i].Row.Acrescimo = ProductPriceHelper.RoundPrice(
                            lines[i].Row.Acrescimo + expected);
                    expectedSum += expected;
                }

                expectedSum = ProductPriceHelper.RoundPrice(expectedSum);
                // Acréscimo manual além da tabela
                var extra = ProductPriceHelper.RoundPrice(delta - expectedSum);
                if (extra > 0.02)
                    AllocateSaleDelta(lines, extra, isAcrescimo: true, parts);
                // Se delta &lt; expected (cobrou a menos no PDV), mantém o esperado na tela
            }

            foreach (var line in lines)
            {
                var row = line.Row;
                var total = ProductPriceHelper.RoundPrice(
                    line.LineSubtotal + row.Acrescimo - row.Discount);
                row.Total = total;
                var (feePct, taxa, liquido) = FeeForSaleAmount(
                    saleFees, line.SaleId, line.PaymentType, total, feeMap);
                row.FeePercent = feePct;
                row.TaxaValor = taxa;
                row.TotalLiquido = liquido;
                row.LucroBruto = ProductPriceHelper.RoundPrice(total - line.CostTotal);
                row.LucroLiquido = ProductPriceHelper.RoundPrice(liquido - line.CostTotal);
            }
        }
    }

    /// <summary>R$ da tabela × quantidade (ex.: 2 maços × R$ 1 = R$ 2).</summary>
    private static double ExpectedLineSurcharge(ProdLineWork line, List<string> paymentLabels)
    {
        var table = PriceTablesService.ResolveForProduct(ProductExtra.Parse(line.ExtraJson));
        if (table is null)
            return 0;

        var fullUnit = 0.0;
        foreach (var pay in paymentLabels)
        {
            if (string.IsNullOrWhiteSpace(pay)) continue;
            var mid = PriceTablesService.ApiLabelToMethodId(pay);
            if (!PriceTablesService.MethodTriggersTable(table, mid))
                continue;
            fullUnit = Math.Max(fullUnit,
                PriceTablesService.CalcUnitSurcharge(line.UnitSale, table, mid));
        }

        if (fullUnit <= 0.009)
        {
            var mid = PriceTablesService.ApiLabelToMethodId(line.PaymentType);
            if (PriceTablesService.MethodTriggersTable(table, mid))
                fullUnit = PriceTablesService.CalcUnitSurcharge(line.UnitSale, table, mid);
        }

        if (fullUnit <= 0.009)
            return 0;
        return ProductPriceHelper.RoundPrice(fullUnit * line.Qty);
    }

    private static void AllocateSaleDelta(
        List<ProdLineWork> lines,
        double delta,
        bool isAcrescimo,
        List<string> paymentLabels)
    {
        var weights = new double[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var table = PriceTablesService.ResolveForProduct(ProductExtra.Parse(line.ExtraJson));
            if (table is null)
            {
                weights[i] = 0;
                continue;
            }

            var fullUnit = 0.0;
            foreach (var pay in paymentLabels)
            {
                if (string.IsNullOrWhiteSpace(pay)) continue;
                var mid = PriceTablesService.ApiLabelToMethodId(pay);
                if (!PriceTablesService.MethodTriggersTable(table, mid))
                    continue;
                fullUnit = Math.Max(fullUnit,
                    PriceTablesService.CalcUnitSurcharge(line.UnitSale, table, mid));
            }

            if (fullUnit <= 0.009)
            {
                var mid = PriceTablesService.ApiLabelToMethodId(lines[0].PaymentType);
                if (PriceTablesService.MethodTriggersTable(table, mid))
                    fullUnit = PriceTablesService.CalcUnitSurcharge(line.UnitSale, table, mid);
            }

            weights[i] = ProductPriceHelper.RoundPrice(fullUnit * line.Qty);
        }

        var weightSum = weights.Sum();
        if (weightSum < 0.009)
        {
            for (var i = 0; i < lines.Count; i++)
                weights[i] = Math.Max(0.01, lines[i].LineSubtotal);
            weightSum = weights.Sum();
        }

        var remaining = delta;
        for (var i = 0; i < lines.Count; i++)
        {
            var share = i == lines.Count - 1
                ? remaining
                : ProductPriceHelper.RoundPrice(delta * (weights[i] / weightSum));
            remaining = ProductPriceHelper.RoundPrice(remaining - share);
            if (share <= 0.009) continue;

            if (isAcrescimo)
                lines[i].Row.Acrescimo = ProductPriceHelper.RoundPrice(lines[i].Row.Acrescimo + share);
            else
                lines[i].Row.Discount = ProductPriceHelper.RoundPrice(lines[i].Row.Discount + share);
        }
    }

    public static MovimentacaoResult ListVendas(
        DateTime dateFrom,
        DateTime dateTo,
        string? paymentType = null,
        int limit = 500)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.MovimentacaoVendas(dateFrom, dateTo, paymentType, limit);
        return ListVendasLocal(dateFrom, dateTo, paymentType, limit);
    }

    public static MovimentacaoResult ListVendasLocal(
        DateTime dateFrom,
        DateTime dateTo,
        string? paymentType = null,
        int limit = 500)
    {
        var (dFrom, dTo) = NormalizePeriod(dateFrom, dateTo);
        var lim = Math.Clamp(limit, 1, 2000);
        var feeMap = PaymentMethodsService.FeeInfoByApiLabel();

        using var conn = DatabaseService.OpenConnection();
        var paymentParts = LoadSalePaymentParts(conn, dFrom, dTo);
        var (faturamento, totalVendas) = CalcFaturamento(conn, dFrom, dTo, paymentType, paymentParts);
        var saleFees = LoadSaleFees(conn, dFrom, dTo, feeMap);
        var costs = LoadSaleCosts(conn, dFrom, dTo);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id,
                   s.created_at,
                   IFNULL(p.name, ''),
                   IFNULL(sel.name, ''),
                   IFNULL(s.payment_type, ''),
                   s.total,
                   (SELECT COUNT(*) FROM sale_items si WHERE si.sale_id = s.id) AS items_count
            FROM sales s
            LEFT JOIN people p ON p.id = s.customer_id
            LEFT JOIN sellers sel ON sel.id = s.seller_id
            WHERE s.cancelled = 0
              AND s.session_date >= $from
              AND s.session_date <= $to
            ORDER BY s.created_at DESC, s.id DESC;
            """;
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));

        var all = new List<MovimentacaoVendaRow>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var saleId = reader.GetInt32(0);
                var payment = reader.IsDBNull(4) ? "" : reader.GetString(4);
                if (!SaleMatchesPayment(saleId, payment, paymentType, paymentParts))
                    continue;

                var total = reader.GetDouble(5);
                var custo = costs.GetValueOrDefault(saleId);
                var (feePct, taxa, liquido) = FeeForSaleAmount(saleFees, saleId, payment, total, feeMap);
                var lucroBruto = ProductPriceHelper.RoundPrice(total - custo);
                var lucroLiq = ProductPriceHelper.RoundPrice(liquido - custo);

                all.Add(new MovimentacaoVendaRow
                {
                    SaleId = saleId,
                    SaleDateBr = FormatBrDateTime(reader.GetString(1)),
                    CustomerName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    SellerName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    PaymentType = payment,
                    ItemsCount = reader.GetInt32(6),
                    CostTotal = custo,
                    Total = total,
                    FeePercent = feePct,
                    TaxaValor = taxa,
                    TotalLiquido = liquido,
                    LucroBruto = lucroBruto,
                    LucroLiquido = lucroLiq,
                });
            }
        }

        var display = all.Take(lim).ToList();
        return BuildResult("vendas", dFrom, dTo, [], display, [], all.Count, faturamento, totalVendas,
            produtosTotais: null, vendasTotais: all);
    }

    public static MovimentacaoResult ListCompras(
        DateTime dateFrom,
        DateTime dateTo,
        int limit = 500)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.MovimentacaoCompras(dateFrom, dateTo, limit);
        return ListComprasLocal(dateFrom, dateTo, limit);
    }

    public static MovimentacaoResult ListComprasLocal(
        DateTime dateFrom,
        DateTime dateTo,
        int limit = 500)
    {
        var (dFrom, dTo) = NormalizePeriod(dateFrom, dateTo);
        var lim = Math.Clamp(limit, 1, 2000);

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.id,
                   p.emission_date,
                   IFNULL(pe.name, ''),
                   TRIM(IFNULL(p.series, '') || '/' || IFNULL(p.number, '')),
                   IFNULL(p.status, ''),
                   p.total,
                   (SELECT COUNT(*) FROM purchase_items pi WHERE pi.purchase_id = p.id) AS items_count
            FROM purchases p
            LEFT JOIN people pe ON pe.id = p.supplier_id
            WHERE p.status != 'cancelada'
              AND p.emission_date >= $from
              AND p.emission_date <= $to
            ORDER BY p.emission_date DESC, p.id DESC;
            """;
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));

        var all = new List<MovimentacaoCompraRow>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                all.Add(new MovimentacaoCompraRow
                {
                    PurchaseId = reader.GetInt32(0),
                    EmissionDateBr = FormatBrDate(reader.IsDBNull(1) ? "" : reader.GetString(1)),
                    SupplierName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Document = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim('/'),
                    Status = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Total = reader.GetDouble(5),
                    ItemsCount = reader.GetInt32(6),
                });
            }
        }

        var display = all.Take(lim).ToList();
        var totalCompras = ProductPriceHelper.RoundPrice(all.Sum(r => r.Total));
        return new MovimentacaoResult
        {
            Tab = "compras",
            Tipo = "compras",
            DateFrom = dFrom,
            DateTo = dTo,
            Compras = display,
            Registros = display.Count,
            TotalRegistros = all.Count,
            Truncated = all.Count > display.Count,
            TotalCompras = totalCompras,
            TotalComprasCount = all.Count,
            TotalValor = totalCompras,
            TotalCusto = totalCompras,
        };
    }

    private static MovimentacaoResult BuildResult(
        string tab,
        DateTime dFrom,
        DateTime dTo,
        IReadOnlyList<MovimentacaoProdutoRow> produtos,
        IReadOnlyList<MovimentacaoVendaRow> vendas,
        IReadOnlyList<MovimentacaoCompraRow> compras,
        int totalRegistros,
        double faturamento,
        int totalVendas,
        IReadOnlyList<MovimentacaoProdutoRow>? produtosTotais = null,
        IReadOnlyList<MovimentacaoVendaRow>? vendasTotais = null)
    {
        double totalValor, totalTaxa, totalLiquido, totalLucroBruto, totalLucro, totalCusto;
        int registros;

        if (tab == "vendas")
        {
            var source = vendasTotais ?? vendas;
            registros = vendas.Count;
            totalValor = ProductPriceHelper.RoundPrice(source.Sum(r => r.Total));
            totalTaxa = ProductPriceHelper.RoundPrice(source.Sum(r => r.TaxaValor));
            totalLiquido = ProductPriceHelper.RoundPrice(source.Sum(r => r.TotalLiquido));
            totalLucroBruto = ProductPriceHelper.RoundPrice(source.Sum(r => r.LucroBruto));
            totalLucro = ProductPriceHelper.RoundPrice(source.Sum(r => r.LucroLiquido));
            totalCusto = ProductPriceHelper.RoundPrice(source.Sum(r => r.CostTotal));
        }
        else
        {
            var source = produtosTotais ?? produtos;
            registros = produtos.Count;
            totalValor = ProductPriceHelper.RoundPrice(source.Sum(r => r.Total));
            totalTaxa = ProductPriceHelper.RoundPrice(source.Sum(r => r.TaxaValor));
            totalLiquido = ProductPriceHelper.RoundPrice(source.Sum(r => r.TotalLiquido));
            totalLucroBruto = ProductPriceHelper.RoundPrice(source.Sum(r => r.LucroBruto));
            totalLucro = ProductPriceHelper.RoundPrice(source.Sum(r => r.LucroLiquido));
            totalCusto = ProductPriceHelper.RoundPrice(source.Sum(r => r.Qty * r.UnitCost));
        }

        return new MovimentacaoResult
        {
            Tab = tab,
            Tipo = "vendas",
            DateFrom = dFrom,
            DateTo = dTo,
            Produtos = produtos as List<MovimentacaoProdutoRow> ?? produtos.ToList(),
            Vendas = vendas as List<MovimentacaoVendaRow> ?? vendas.ToList(),
            Compras = compras as List<MovimentacaoCompraRow> ?? compras.ToList(),
            Registros = registros,
            TotalRegistros = totalRegistros,
            Truncated = totalRegistros > registros,
            TotalFaturamento = faturamento,
            TotalVendas = totalVendas,
            TotalValor = totalValor,
            TotalTaxa = totalTaxa,
            TotalLiquido = totalLiquido,
            TotalLucroBruto = totalLucroBruto,
            TotalLucro = totalLucro,
            TotalCusto = totalCusto,
        };
    }

    private static string FormatBrDate(string iso)
    {
        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)
            || DateTime.TryParse(iso, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
            return dt.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
        return iso;
    }

    private static (DateTime From, DateTime To) NormalizePeriod(DateTime dateFrom, DateTime dateTo)
    {
        var dFrom = dateFrom.Date;
        var dTo = dateTo.Date;
        if (dFrom > dTo)
            (dFrom, dTo) = (dTo, dFrom);
        return (dFrom, dTo);
    }

    private static (double Faturamento, int Count) CalcFaturamento(
        SqliteConnection conn,
        DateTime dFrom,
        DateTime dTo,
        string? paymentType,
        Dictionary<int, List<string>> paymentParts)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, total, IFNULL(payment_type, '')
            FROM sales
            WHERE cancelled = 0
              AND session_date >= $from
              AND session_date <= $to;
            """;
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));

        double total = 0;
        var count = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var saleId = reader.GetInt32(0);
            var payment = reader.IsDBNull(2) ? "" : reader.GetString(2);
            if (!SaleMatchesPayment(saleId, payment, paymentType, paymentParts))
                continue;
            total += reader.GetDouble(1);
            count++;
        }
        return (ProductPriceHelper.RoundPrice(total), count);
    }

    private static Dictionary<int, List<string>> LoadSalePaymentParts(
        SqliteConnection conn, DateTime dFrom, DateTime dTo)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT cm.ref_id, IFNULL(cm.payment_type, '')
            FROM cash_movements cm
            JOIN sales s ON s.id = cm.ref_id
            WHERE cm.ref_type = 'sale'
              AND cm.kind IN ('venda', 'venda_fiado')
              AND s.cancelled = 0
              AND s.session_date >= $from
              AND s.session_date <= $to;
            """;
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));

        var map = new Dictionary<int, List<string>>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var saleId = reader.GetInt32(0);
            var payment = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (!map.TryGetValue(saleId, out var list))
            {
                list = [];
                map[saleId] = list;
            }
            list.Add(payment);
        }
        return map;
    }

    private static Dictionary<int, double> LoadSaleCosts(
        SqliteConnection conn, DateTime dFrom, DateTime dTo)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT si.sale_id,
                   si.quantity,
                   si.unit_price,
                   IFNULL(p.cost_price, 0),
                   IFNULL(si.product_name, ''),
                   IFNULL(p.extra_json, ''),
                   IFNULL(p.group_name, ''),
                   si.cost_at_sale
            FROM sale_items si
            JOIN sales s ON s.id = si.sale_id
            LEFT JOIN products p ON p.id = si.product_id
            WHERE s.cancelled = 0
              AND s.session_date >= $from
              AND s.session_date <= $to;
            """;
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));

        var map = new Dictionary<int, double>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var saleId = reader.GetInt32(0);
            var qty = reader.GetDouble(1);
            var unitSale = reader.GetDouble(2);
            var catalogCost = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);
            var name = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var extra = ProductExtra.Parse(reader.IsDBNull(5) ? null : reader.GetString(5));
            var group = reader.IsDBNull(6) ? "" : reader.GetString(6);
            var costAtSale = HistoricalSaleCostRules.ReadCostAtSale(reader, 7);
            var line = HistoricalSaleCostRules.ResolveLine(
                qty, costAtSale, catalogCost, unitSale, name, group, extra);
            var lineCost = ProductPriceHelper.RoundPrice(line.TotalCost);
            map[saleId] = ProductPriceHelper.RoundPrice(
                (map.TryGetValue(saleId, out var prev) ? prev : 0) + lineCost);
        }
        return map;
    }

    /// <summary>Taxa total da venda (considera pagamento misto via cash_movements).</summary>
    private static Dictionary<int, (double TaxaTotal, double BaseTotal)> LoadSaleFees(
        SqliteConnection conn, DateTime dFrom, DateTime dTo, Dictionary<string, PaymentFeeInfo> feeMap)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT cm.ref_id, IFNULL(cm.payment_type, ''), IFNULL(cm.amount_in, 0)
            FROM cash_movements cm
            JOIN sales s ON s.id = cm.ref_id
            WHERE cm.ref_type = 'sale'
              AND cm.kind = 'venda'
              AND s.cancelled = 0
              AND s.session_date >= $from
              AND s.session_date <= $to;
            """;
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));

        var agg = new Dictionary<int, (double Taxa, double Base)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var saleId = reader.GetInt32(0);
            var payment = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var amt = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
            var info = FeeInfoFor(payment, feeMap);
            var taxa = PaymentMethodsService.CalcFeeAmount(amt, info.FeePercent, info.FeeFixed);
            if (!agg.TryGetValue(saleId, out var cur))
                cur = (0, 0);
            agg[saleId] = (
                ProductPriceHelper.RoundPrice(cur.Taxa + taxa),
                ProductPriceHelper.RoundPrice(cur.Base + amt));
        }
        return agg;
    }

    private static (double FeePct, double Taxa, double Liquido) FeeForSaleAmount(
        Dictionary<int, (double TaxaTotal, double BaseTotal)> saleFees,
        int saleId,
        string paymentType,
        double amount,
        Dictionary<string, PaymentFeeInfo> feeMap)
    {
        if (saleFees.TryGetValue(saleId, out var fees) && fees.BaseTotal > 0.009)
        {
            var share = amount / fees.BaseTotal;
            var taxa = ProductPriceHelper.RoundPrice(fees.TaxaTotal * share);
            var liquido = ProductPriceHelper.RoundPrice(amount - taxa);
            var pct = amount > 0.009
                ? ProductPriceHelper.RoundPrice(taxa / amount * 100)
                : 0;
            return (pct, taxa, liquido);
        }

        var info = FeeInfoFor(paymentType, feeMap);
        var taxaSingle = PaymentMethodsService.CalcFeeAmount(amount, info.FeePercent, info.FeeFixed);
        return (info.FeePercent, taxaSingle, ProductPriceHelper.RoundPrice(amount - taxaSingle));
    }

    private static PaymentFeeInfo FeeInfoFor(string? paymentType, Dictionary<string, PaymentFeeInfo> feeMap)
    {
        var forma = NormalizeForma(paymentType);
        if (feeMap.TryGetValue(forma, out var info))
            return info;

        var low = (paymentType ?? "").ToLowerInvariant();
        if (low.Contains('+') || low.Contains("misto"))
            return new PaymentFeeInfo();

        foreach (var kv in feeMap)
        {
            if (low.Contains(kv.Key.ToLowerInvariant()))
                return kv.Value;
        }
        return new PaymentFeeInfo();
    }

    private static double FeePercentFor(string? paymentType, Dictionary<string, PaymentFeeInfo> feeMap) =>
        FeeInfoFor(paymentType, feeMap).FeePercent;

    private static bool SaleMatchesPayment(
        int saleId,
        string salePayment,
        string? filter,
        Dictionary<int, List<string>> paymentParts)
    {
        if (!IsPaymentFilterActive(filter))
            return true;

        var forma = NormalizeForma(filter);
        if (NormalizeForma(salePayment) == forma)
            return true;

        if (paymentParts.TryGetValue(saleId, out var parts) && parts.Count > 0)
            return parts.Any(p => NormalizeForma(p) == forma);

        var needle = (filter ?? "").Trim().ToUpperInvariant();
        return (salePayment ?? "").ToUpperInvariant().Contains(needle);
    }

    private static bool IsPaymentFilterActive(string? paymentType)
    {
        if (string.IsNullOrWhiteSpace(paymentType))
            return false;
        var key = paymentType.Trim().ToUpperInvariant();
        return key is not ("TODAS" or "TODOS" or "*");
    }

    private static (double Discount, double Acrescimo) LineDiscountAcrescimo(
        double qty, double unitSale, double subtotal)
    {
        var baseTotal = ProductPriceHelper.RoundPrice(qty * unitSale);
        var discount = ProductPriceHelper.RoundPrice(Math.Max(0, baseTotal - subtotal));
        var acrescimo = ProductPriceHelper.RoundPrice(Math.Max(0, subtotal - baseTotal));
        return (discount, acrescimo);
    }

    private static string NormalizeForma(string? paymentType)
    {
        var s = (paymentType ?? "").Trim();
        if (string.IsNullOrEmpty(s))
            return "—";
        var low = s.ToLowerInvariant();
        if (low is "dinheiro" or "cash" or "din") return "Dinheiro";
        if (low is "pix") return "Pix";
        if (low.Contains("debito") || low.Contains("débito") || low is "deb") return "Cartão Débito";
        if (low.Contains("credito") || low.Contains("crédito") || low is "cred") return "Cartão Crédito";
        if (low.Contains("fiado") || low.Contains("prazo")) return "Fiado";
        return s.Length > 40 ? s[..40] : s;
    }

    private static string FormatBrDateTime(string iso) =>
        DateBrHelper.FormatUtcToBrazil(iso, "dd/MM/yyyy HH:mm");
}
