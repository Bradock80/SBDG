using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>Consultas históricas do PDV (lista, detalhe, resumo do dia).</summary>
public static class PdvQueryService
{
    public static IReadOnlyList<PdvSaleListRow> ListSales(DateTime? sessionDate = null, bool includeCancelled = false)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.ListPdvSales(sessionDate, includeCancelled);
        return ListSalesLocal(sessionDate, includeCancelled);
    }

    public static IReadOnlyList<PdvSaleListRow> ListSalesLocal(DateTime? sessionDate = null, bool includeCancelled = false)
    {
        var (from, to) = ResolveSalesDateRange(sessionDate);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = """
            SELECT s.id, s.session_date, s.total, s.payment_type, s.cancelled, s.created_at, s.cash_received, s.change_amount,
                   p.name AS customer_name,
                   (SELECT COUNT(*) FROM sale_items si WHERE si.sale_id = s.id) AS items_count,
                   sel.name AS seller_name,
                   (SELECT pi.status FROM pix_intents pi WHERE pi.sale_id = s.id ORDER BY pi.id DESC LIMIT 1) AS pix_status
            FROM sales s
            LEFT JOIN people p ON p.id = s.customer_id
            LEFT JOIN sellers sel ON sel.id = s.seller_id
            WHERE s.session_date >= $from AND s.session_date <= $to
            """;
        if (!includeCancelled)
            sql += " AND s.cancelled = 0";
        sql += """

            ORDER BY s.created_at DESC, s.id DESC
            LIMIT 500;
            """;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        var rows = new List<PdvSaleListRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            var paymentType = reader.GetString(3);
            var cashRecv = reader.IsDBNull(6) ? (double?)null : reader.GetDouble(6);
            var change = reader.IsDBNull(7) ? (double?)null : reader.GetDouble(7);
            rows.Add(new PdvSaleListRow
            {
                Id = id,
                SessionDate = reader.GetString(1),
                Total = reader.GetDouble(2),
                PaymentType = paymentType,
                Cancelled = reader.GetInt32(4) != 0,
                CreatedAtBr = FormatBrDateTime(reader.GetString(5)),
                CustomerName = reader.IsDBNull(8) ? null : reader.GetString(8),
                ItemsCount = reader.GetInt32(9),
                SellerName = reader.IsDBNull(10) ? null : reader.GetString(10),
                PaymentLabel = FormatPaymentLabel(paymentType, cashRecv, change),
                PixIntentStatus = reader.IsDBNull(11) ? null : reader.GetString(11),
            });
        }
        return rows;
    }

    /// <summary>
    /// Com caixa aberto de ontem pra hoje: usa o turno inteiro. Com data explícita: só aquele dia.
    /// </summary>
    private static (DateTime From, DateTime To) ResolveSalesDateRange(DateTime? sessionDate)
    {
        if (sessionDate is DateTime explicitDate)
        {
            var d = explicitDate.Date;
            return (d, d);
        }

        var range = CashService.GetPdvSalesDateRange();
        return (range.From, range.To);
    }

    private static string FormatSalesPeriodLabel(DateTime from, DateTime to) =>
        from.Date == to.Date
            ? from.ToString("dd/MM/yyyy")
            : $"{from:dd/MM/yyyy} a {to:dd/MM/yyyy}";

    public static PdvSaleDetail GetSaleDetail(int saleId)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.GetPdvSaleDetail(saleId);
        return GetSaleDetailLocal(saleId);
    }

    public static PdvSaleDetail GetSaleDetailLocal(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.session_date, s.total, s.payment_type, s.cancelled, s.created_at,
                   s.cash_received, s.change_amount, p.name, sel.name AS seller_name, s.customer_id
            FROM sales s
            LEFT JOIN people p ON p.id = s.customer_id
            LEFT JOIN sellers sel ON sel.id = s.seller_id
            WHERE s.id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new PdvException("Venda não encontrada.");

        var id = reader.GetInt32(0);
        var sessionDate = reader.GetString(1);
        var total = reader.GetDouble(2);
        var paymentType = reader.GetString(3);
        var cancelled = reader.GetInt32(4) != 0;
        var createdAt = reader.GetString(5);
        double? cashReceived = reader.IsDBNull(6) ? null : reader.GetDouble(6);
        double? changeAmount = reader.IsDBNull(7) ? null : reader.GetDouble(7);
        var customerName = reader.IsDBNull(8) ? null : reader.GetString(8);
        var sellerName = reader.IsDBNull(9) ? null : reader.GetString(9);
        int? customerId = reader.IsDBNull(10) ? null : reader.GetInt32(10);
        reader.Close();

        var detail = new PdvSaleDetail
        {
            Id = id,
            SessionDate = sessionDate,
            Total = total,
            PaymentType = paymentType,
            Cancelled = cancelled,
            CreatedAtBr = FormatBrDateTime(createdAt),
            CashReceived = cashReceived,
            ChangeAmount = changeAmount,
            CustomerName = customerName,
            SellerName = sellerName,
            CustomerPersonId = customerId,
            PaymentLabel = FormatPaymentLabel(paymentType, cashReceived, changeAmount),
            Payments = LoadSalePayments(conn, saleId, paymentType, total),
            Items = LoadSaleItems(conn, saleId),
        };
        return detail;
    }

    public static PdvResumoDia GetResumoDia(DateTime? sessionDate = null)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.GetPdvResumoDia(sessionDate);
        return GetResumoDiaLocal(sessionDate);
    }

    public static PdvResumoDia GetResumoDiaLocal(DateTime? sessionDate = null)
    {
        var (from, to) = ResolveSalesDateRange(sessionDate);
        var d = to; // dia de referência do caixa (hoje / data pedida)
        using var conn = DatabaseService.OpenConnection();

        var op = CashService.GetOperacaoView(d);
        var abertoDesde = string.IsNullOrWhiteSpace(op.OpenedAtBr)
            ? op.OpenedTimeBr
            : string.IsNullOrWhiteSpace(op.OpenedTimeBr)
                ? op.OpenedAtBr
                : $"{op.OpenedAtBr} {op.OpenedTimeBr}";
        var periodLabel = FormatSalesPeriodLabel(from, to);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.total, s.payment_type, s.cancelled,
                   si.product_id, si.quantity, si.subtotal, si.product_name,
                   IFNULL(p.group_name, '') AS group_name, IFNULL(p.cost_price, 0) AS cost_price,
                   IFNULL(p.extra_json, '') AS extra_json
            FROM sales s
            JOIN sale_items si ON si.sale_id = s.id
            LEFT JOIN products p ON p.id = si.product_id
            WHERE s.session_date >= $from AND s.session_date <= $to;
            """;
        cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));

        var salesOk = new HashSet<int>();
        var salesCancel = 0;
        var grupos = new Dictionary<string, (double total, double lucro, double qty)>(StringComparer.OrdinalIgnoreCase);
        var formas = new Dictionary<string, (double total, int count)>(StringComparer.OrdinalIgnoreCase);
        var top = new Dictionary<int, (string name, double qty, double total)>();
        double faturamento = 0;
        double lucro = 0;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var saleId = reader.GetInt32(0);
            var saleTotal = reader.GetDouble(1);
            var paymentType = reader.GetString(2);
            var cancelled = reader.GetInt32(3) != 0;
            if (cancelled)
            {
                salesCancel++;
                continue;
            }

            salesOk.Add(saleId);
            var subtotal = reader.GetDouble(6);
            var qty = reader.GetDouble(5);
            var rawGroup = reader.GetString(8).Trim();
            var group = string.IsNullOrWhiteSpace(rawGroup) ? "Sem grupo" : rawGroup;
            var productName = reader.GetString(7);
            var catalogCost = reader.GetDouble(9);
            var extra = ProductExtra.Parse(reader.IsDBNull(10) ? null : reader.GetString(10));
            // Mesmo cálculo das outras telas: custo cadastrado em fardo/cartela vira custo unitário.
            var unitSale = qty > 0 ? ProductPriceHelper.RoundPrice(subtotal / qty) : 0;
            var unitCost = ProductPriceHelper.UnitCostForSoldLine(
                catalogCost, unitSale, extra, productName, rawGroup);
            var costTotal = ProductPriceHelper.RoundPrice(unitCost * qty);
            var lineLucro = ProductPriceHelper.RoundPrice(subtotal - costTotal);

            faturamento += subtotal;
            lucro += lineLucro;

            if (!grupos.TryGetValue(group, out var g))
                g = (0, 0, 0);
            g.total += subtotal;
            g.lucro += lineLucro;
            g.qty += qty;
            grupos[group] = g;

            var pid = reader.GetInt32(4);
            if (!top.TryGetValue(pid, out var t))
                t = (productName, 0, 0);
            t.qty += qty;
            t.total += subtotal;
            top[pid] = t;
        }
        reader.Close();

        // Local: não reentra no roteamento Rede Loja (ListSales → Client).
        foreach (var sale in ListSalesLocal(sessionDate, includeCancelled: false))
        {
            var key = ExpandPaymentForma(sale.PaymentType);
            if (!formas.TryGetValue(key, out var f))
                f = (0, 0);
            f.total += sale.Total;
            f.count++;
            formas[key] = f;
        }

        faturamento = ProductPriceHelper.RoundPrice(faturamento);
        lucro = ProductPriceHelper.RoundPrice(lucro);
        var qtd = salesOk.Count;
        var margem = faturamento > 0 ? ProductPriceHelper.RoundPrice(lucro / faturamento * 100) : 0;
        var ticket = qtd > 0 ? ProductPriceHelper.RoundPrice(faturamento / qtd) : 0;

        var fiado = formas
            .Where(kv => kv.Key.Contains("Fiado", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var fiadoTotal = ProductPriceHelper.RoundPrice(fiado.Sum(kv => kv.Value.total));
        var fiadoCount = fiado.Sum(kv => kv.Value.count);

        return new PdvResumoDia
        {
            SessionDate = periodLabel,
            CaixaOpen = op.IsOperational,
            CaixaAbertoDesde = abertoDesde,
            CaixaInfo = op.IsOperational
                ? $"Caixa aberto desde {abertoDesde} — saldo gaveta R$ {op.SaldoFinalGaveta:N2}"
                : "Caixa não aberto",
            EntradaCaixa = op.SaldoInicial,
            EntradasCaixa = ProductPriceHelper.RoundPrice(op.EntradasCaixa),
            SaidasCaixa = ProductPriceHelper.RoundPrice(op.SaidasCaixa),
            SaldoGaveta = ProductPriceHelper.RoundPrice(op.SaldoFinalGaveta),
            Faturamento = faturamento,
            LucroReal = lucro,
            MargemPercent = margem,
            QtdVendas = qtd,
            TicketMedio = ticket,
            QtdCancelados = salesCancel,
            FiadoTotal = fiadoTotal,
            FiadoCount = fiadoCount,
            Grupos = grupos.Select(kv => new PdvResumoGrupoRow
            {
                GroupName = kv.Key,
                Total = ProductPriceHelper.RoundPrice(kv.Value.total),
                Lucro = ProductPriceHelper.RoundPrice(kv.Value.lucro),
                Qty = ProductPriceHelper.RoundPrice(kv.Value.qty),
                MargemPercent = kv.Value.total > 0
                    ? ProductPriceHelper.RoundPrice(kv.Value.lucro / kv.Value.total * 100) : 0,
            }).OrderByDescending(g => g.Total).ToList(),
            Formas = formas.Select(kv => new PdvResumoFormaRow
            {
                Forma = kv.Key,
                Total = ProductPriceHelper.RoundPrice(kv.Value.total),
                Count = kv.Value.count,
            }).OrderByDescending(f => f.Total).ToList(),
            TopProdutos = top.Values
                .OrderByDescending(t => t.total)
                .Take(15)
                .Select(t => new PdvResumoTopRow
                {
                    ProductName = t.name,
                    Qty = ProductPriceHelper.RoundPrice(t.qty),
                    Total = ProductPriceHelper.RoundPrice(t.total),
                }).ToList(),
        };
    }

    /// <summary>Loader compartilhado com mutações do PDV (Swap / ChangeSalePayment).</summary>
    internal static List<PdvSaleItemRow> LoadSaleItems(SqliteConnection conn, int saleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, product_id, product_code, product_name, unit, quantity, unit_price, subtotal,
                   cost_at_sale
            FROM sale_items WHERE sale_id = $id ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        var list = new List<PdvSaleItemRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PdvSaleItemRow
            {
                Id = reader.GetInt32(0),
                ProductId = reader.GetInt32(1),
                ProductCode = reader.GetString(2),
                ProductName = reader.GetString(3),
                Unit = reader.GetString(4),
                Quantity = reader.GetDouble(5),
                UnitPrice = reader.GetDouble(6),
                Subtotal = reader.GetDouble(7),
                CostAtSale = reader.IsDBNull(8) ? null : reader.GetDouble(8),
            });
        }
        return list;
    }

    /// <summary>Loader compartilhado com mutações do PDV (Swap / ChangeSalePayment).</summary>
    internal static List<PdvPaymentPart> LoadSalePayments(SqliteConnection conn, int saleId, string paymentType, double total)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT payment_type, amount_in FROM cash_movements
            WHERE ref_type = 'sale' AND ref_id = $id AND kind IN ('venda', 'venda_fiado')
            ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        var list = new List<PdvPaymentPart>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PdvPaymentPart
            {
                PaymentType = NormalizePayment(reader.GetString(0)),
                Amount = reader.GetDouble(1),
            });
        }
        if (list.Count == 0)
            list.Add(new PdvPaymentPart { PaymentType = NormalizePayment(paymentType), Amount = total });
        return list;
    }

    private static string FormatBrDateTime(string? iso) =>
        DateBrHelper.FormatUtcToBrazil(iso, "dd/MM/yyyy HH:mm");

    private static string FormatPaymentLabel(string paymentType, double? cashReceived, double? change)
    {
        var pt = paymentType.Trim();
        if (cashReceived is not null && change is not null && change > 0.009)
            return $"{pt} · receb. R$ {cashReceived:N2} · troco R$ {change:N2}";
        return pt;
    }

    private static string ExpandPaymentForma(string paymentType)
    {
        var pt = paymentType.Trim();
        if (pt.Contains('+', StringComparison.Ordinal))
            return pt;
        return NormalizePayment(pt);
    }

    private static string NormalizePayment(string? paymentType) =>
        PaymentMethodsService.NormalizeToApiLabel(paymentType);
}
