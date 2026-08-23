using System.Globalization;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public static class ReportsService
{
    public static MaisVendidosResult ListMaisVendidos(
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        int limit = 100)
    {
        var dFrom = (dateFrom ?? DateTime.Today).Date;
        var dTo = (dateTo ?? DateTime.Today).Date;
        if (dFrom > dTo)
            (dFrom, dTo) = (dTo, dFrom);

        var lim = Math.Clamp(limit, 1, 500);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT si.product_id,
                   IFNULL(si.product_code, ''),
                   IFNULL(si.product_name, ''),
                   IFNULL(p.group_name, ''),
                   SUM(si.quantity) AS qty,
                   SUM(si.subtotal) AS total
            FROM sale_items si
            JOIN sales s ON s.id = si.sale_id
            LEFT JOIN products p ON p.id = si.product_id
            WHERE s.cancelled = 0
              AND s.session_date >= $from
              AND s.session_date <= $to
            GROUP BY si.product_id, si.product_code, si.product_name, p.group_name
            ORDER BY qty DESC, total DESC, si.product_name ASC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$limit", lim);

        var rows = new List<MaisVendidoRow>();
        using var reader = cmd.ExecuteReader();
        var pos = 0;
        double totalQty = 0;
        double totalValor = 0;
        while (reader.Read())
        {
            pos++;
            var qty = reader.GetDouble(4);
            var total = reader.GetDouble(5);
            totalQty += qty;
            totalValor += total;
            rows.Add(new MaisVendidoRow
            {
                Posicao = pos,
                ProductId = reader.GetInt32(0),
                Code = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                GroupName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Qty = ProductPriceHelper.RoundPrice(qty),
                Total = ProductPriceHelper.RoundPrice(total),
            });
        }

        return new MaisVendidosResult
        {
            Rows = rows,
            Registros = rows.Count,
            TotalQty = ProductPriceHelper.RoundPrice(totalQty),
            TotalValor = ProductPriceHelper.RoundPrice(totalValor),
            DateFrom = dFrom,
            DateTo = dTo,
        };
    }

    public static IReadOnlyList<PdvSaleListRow> ListVendasPdv(
        DateTime dateFrom,
        DateTime dateTo,
        bool includeCancelled = true,
        int limit = 500)
    {
        var dFrom = dateFrom.Date;
        var dTo = dateTo.Date;
        if (dFrom > dTo)
            (dFrom, dTo) = (dTo, dFrom);

        var lim = Math.Clamp(limit, 1, 500);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = """
            SELECT s.id, s.session_date, s.total, s.payment_type, s.cancelled, s.created_at,
                   s.cash_received, s.change_amount,
                   p.name AS customer_name,
                   (SELECT COUNT(*) FROM sale_items si WHERE si.sale_id = s.id) AS items_count,
                   sel.name AS seller_name
            FROM sales s
            LEFT JOIN people p ON p.id = s.customer_id
            LEFT JOIN sellers sel ON sel.id = s.seller_id
            WHERE s.session_date >= $from AND s.session_date <= $to
            """;
        if (!includeCancelled)
            sql += " AND s.cancelled = 0";
        sql += """

            ORDER BY s.created_at DESC, s.id DESC
            LIMIT $limit;
            """;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$limit", lim);

        var rows = new List<PdvSaleListRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var paymentType = reader.GetString(3);
            var cashRecv = reader.IsDBNull(6) ? (double?)null : reader.GetDouble(6);
            var change = reader.IsDBNull(7) ? (double?)null : reader.GetDouble(7);
            rows.Add(new PdvSaleListRow
            {
                Id = reader.GetInt32(0),
                SessionDate = reader.GetString(1),
                Total = reader.GetDouble(2),
                PaymentType = paymentType,
                Cancelled = reader.GetInt32(4) != 0,
                CreatedAtBr = FormatBrDateTime(reader.GetString(5)),
                CustomerName = reader.IsDBNull(8) ? null : reader.GetString(8),
                ItemsCount = reader.GetInt32(9),
                SellerName = reader.IsDBNull(10) ? null : reader.GetString(10),
                PaymentLabel = FormatPaymentLabel(paymentType, cashRecv, change),
            });
        }
        return rows;
    }

    public static CurvaAbcResult ListCurvaAbc(
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        int limit = 500)
    {
        var dFrom = (dateFrom ?? DateTime.Today.AddDays(-30)).Date;
        var dTo = (dateTo ?? DateTime.Today).Date;
        if (dFrom > dTo)
            (dFrom, dTo) = (dTo, dFrom);

        var lim = Math.Clamp(limit, 1, 500);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT si.product_id,
                   IFNULL(si.product_code, ''),
                   IFNULL(si.product_name, ''),
                   IFNULL(p.group_name, ''),
                   SUM(si.quantity) AS qty,
                   SUM(si.subtotal) AS total,
                   IFNULL(p.stock, 0),
                   IFNULL(p.cost_price, 0)
            FROM sale_items si
            JOIN sales s ON s.id = si.sale_id
            LEFT JOIN products p ON p.id = si.product_id
            WHERE s.cancelled = 0
              AND s.session_date >= $from
              AND s.session_date <= $to
            GROUP BY si.product_id, si.product_code, si.product_name, p.group_name, p.stock, p.cost_price
            ORDER BY total DESC, qty DESC, si.product_name ASC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$limit", lim);

        var days = Math.Max(1, (dTo - dFrom).TotalDays + 1);
        var raw = new List<(int Pid, string Code, string Name, string Group, double Qty, double Total, double Stock, double Cost)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                raw.Add((
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? "" : reader.GetString(2),
                    reader.IsDBNull(3) ? "" : reader.GetString(3),
                    reader.GetDouble(4),
                    reader.GetDouble(5),
                    reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
                    reader.IsDBNull(7) ? 0 : reader.GetDouble(7)));
            }
        }

        var totalValor = ProductPriceHelper.RoundPrice(raw.Sum(r => r.Total));
        var rows = new List<CurvaAbcRow>();
        var acumulado = 0.0;
        var countA = 0;
        var countB = 0;
        var countC = 0;
        var pos = 0;
        foreach (var r in raw)
        {
            pos++;
            acumulado += r.Total;
            var part = totalValor > 0.009 ? (r.Total / totalValor) * 100.0 : 0;
            var acumPct = totalValor > 0.009 ? (acumulado / totalValor) * 100.0 : 0;
            var prevAcum = acumulado - r.Total;
            var prevPct = totalValor > 0.009 ? (prevAcum / totalValor) * 100.0 : 0;
            string classe;
            if (prevPct < 80.0)
                classe = "A";
            else if (prevPct < 95.0)
                classe = "B";
            else
                classe = "C";

            switch (classe)
            {
                case "A": countA++; break;
                case "B": countB++; break;
                default: countC++; break;
            }

            var avgDaily = r.Qty / days;
            var daysStock = avgDaily > 0.0001 ? r.Stock / avgDaily : 0;

            rows.Add(new CurvaAbcRow
            {
                Posicao = pos,
                ProductId = r.Pid,
                Code = r.Code,
                Name = r.Name,
                GroupName = r.Group,
                Qty = ProductPriceHelper.RoundPrice(r.Qty),
                Total = ProductPriceHelper.RoundPrice(r.Total),
                ParticipacaoPercent = ProductPriceHelper.RoundPrice(part),
                AcumuladoPercent = ProductPriceHelper.RoundPrice(acumPct),
                Classe = classe,
                Stock = ProductPriceHelper.RoundPrice(r.Stock),
                CostPrice = ProductPriceHelper.RoundPrice(r.Cost),
                DaysOfStock = ProductPriceHelper.RoundPrice(daysStock),
                CapitalParado = ProductPriceHelper.RoundPrice(r.Stock * r.Cost),
            });
        }

        return new CurvaAbcResult
        {
            Rows = rows,
            Registros = rows.Count,
            TotalValor = totalValor,
            CountA = countA,
            CountB = countB,
            CountC = countC,
            DateFrom = dFrom,
            DateTo = dTo,
        };
    }

    public static EstoqueMinimoResult ListEstoqueMinimo()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(code, ''), IFNULL(name, ''),
                   (IFNULL(stock, 0) + IFNULL(stock_fridge, 0)), IFNULL(min_stock, 0)
            FROM products
            WHERE IFNULL(active, 1) = 1
              AND (IFNULL(stock, 0) + IFNULL(stock_fridge, 0)) <= IFNULL(min_stock, 0)
            ORDER BY (IFNULL(min_stock, 0) - (IFNULL(stock, 0) + IFNULL(stock_fridge, 0))) DESC, name ASC;
            """;

        var rows = new List<EstoqueMinimoRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var stock = reader.GetDouble(2);
            var min = reader.GetDouble(3);
            var sugestao = Math.Max(0, Math.Ceiling(min - stock));
            if (sugestao < 0.009 && stock <= min)
                sugestao = Math.Max(1, Math.Ceiling(min));

            rows.Add(new EstoqueMinimoRow
            {
                Code = reader.IsDBNull(0) ? "" : reader.GetString(0),
                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Stock = ProductPriceHelper.RoundPrice(stock),
                MinStock = min,
                SugestaoCompra = sugestao,
            });
        }

        return new EstoqueMinimoResult { Rows = rows, Registros = rows.Count };
    }

    /// <summary>
    /// Agenda de fiados: vencimento estimado = última venda + 30 dias (prazo típico do depósito).
    /// HorizontDays filtra o que vence até hoje+N (inclui já vencidos).
    /// </summary>
    public static PrevisaoRecebimentoResult ListPrevisaoRecebimento(int horizonDays = 30)
    {
        var days = horizonDays is 7 or 15 or 30 ? horizonDays : 30;
        var limit = DateTime.Today.AddDays(days);
        var list = FiadoService.ListContas(somenteSaldo: true);
        var rows = new List<PrevisaoRecebimentoRow>();

        foreach (var c in list.Rows)
        {
            if (c.Orphan || c.CustomerId <= 0 || c.Balance <= 0.005)
                continue;

            DateTime? lastSale = null;
            if (!string.IsNullOrWhiteSpace(c.LastSaleBr)
                && (DateTime.TryParseExact(c.LastSaleBr, ["dd/MM/yyyy HH:mm", "dd/MM/yyyy"],
                        CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out var parsed)
                    || DateTime.TryParse(c.LastSaleBr, CultureInfo.GetCultureInfo("pt-BR"),
                        DateTimeStyles.None, out parsed)))
                lastSale = parsed.Date;

            var due = (lastSale ?? DateTime.Today).AddDays(30);
            if (due > limit && due >= DateTime.Today)
                continue;

            rows.Add(new PrevisaoRecebimentoRow
            {
                CustomerId = c.CustomerId,
                CustomerName = c.CustomerName,
                Phone = c.Phone,
                Balance = c.Balance,
                LastSale = lastSale,
                DueEstimated = due,
                IsOverdue = due.Date < DateTime.Today,
            });
        }

        rows = rows
            .OrderBy(r => r.DueEstimated)
            .ThenByDescending(r => r.Balance)
            .ToList();

        return new PrevisaoRecebimentoResult
        {
            Rows = rows,
            Registros = rows.Count,
            TotalProjetado = ProductPriceHelper.RoundPrice(rows.Sum(r => r.Balance)),
            TotalVencido = ProductPriceHelper.RoundPrice(rows.Where(r => r.IsOverdue).Sum(r => r.Balance)),
            HorizontDays = days,
        };
    }

    public static FechamentoConsolidadoResult GetFechamentoConsolidado(
        DateTime? dateFrom = null,
        DateTime? dateTo = null)
    {
        var dFrom = (dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var dTo = (dateTo ?? DateTime.Today).Date;
        if (dFrom > dTo)
            (dFrom, dTo) = (dTo, dFrom);

        using var conn = DatabaseService.OpenConnection();

        double totalFaturado = 0;
        double totalAVista = 0;
        double totalFiado = 0;
        var qtdVendas = 0;
        var porForma = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        var sales = new List<(int Id, double Total, string PaymentType)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, total, IFNULL(payment_type, '')
                FROM sales
                WHERE cancelled = 0
                  AND session_date >= $from
                  AND session_date <= $to;
                """;
            cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                sales.Add((reader.GetInt32(0), reader.GetDouble(1), reader.GetString(2)));
        }

        foreach (var (saleId, total, paymentType) in sales)
        {
            qtdVendas++;
            var faturado = ProductPriceHelper.RoundPrice(total);
            totalFaturado += faturado;

            var fiadoAmt = SaleFiadoAmount(conn, saleId, paymentType);
            var vistaAmt = ProductPriceHelper.RoundPrice(Math.Max(0, faturado - fiadoAmt));
            totalFiado += fiadoAmt;
            totalAVista += vistaAmt;

            if (fiadoAmt > 0.009 && vistaAmt < 0.009)
            {
                porForma["Fiado"] = porForma.GetValueOrDefault("Fiado") + fiadoAmt;
            }
            else if (paymentType.Contains('+', StringComparison.Ordinal)
                     || paymentType.Contains("misto", StringComparison.OrdinalIgnoreCase))
            {
                // misto: aloca fiado + resto como "Misto/à vista"
                if (fiadoAmt > 0.009)
                    porForma["Fiado"] = porForma.GetValueOrDefault("Fiado") + fiadoAmt;
                if (vistaAmt > 0.009)
                {
                    var label = NormalizeForma(paymentType);
                    porForma[label] = porForma.GetValueOrDefault(label) + vistaAmt;
                }
            }
            else
            {
                var label = fiadoAmt > 0.009 && vistaAmt < 0.009
                    ? "Fiado"
                    : NormalizeForma(paymentType);
                porForma[label] = porForma.GetValueOrDefault(label) + faturado;
            }
        }

        totalFaturado = ProductPriceHelper.RoundPrice(totalFaturado);
        totalAVista = ProductPriceHelper.RoundPrice(totalAVista);
        totalFiado = ProductPriceHelper.RoundPrice(totalFiado);

        var periodCmv = HistoricalSaleCostRules.SumNonCancelledBySession(
            conn, dFrom.ToString("yyyy-MM-dd"), dTo.ToString("yyyy-MM-dd"));
        var cmv = periodCmv.Total;

        double recebidoFiado = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COALESCE(SUM(amount), 0)
                FROM fiado_payments
                WHERE reversed = 0
                  AND payment_date >= $from
                  AND payment_date <= $to;
                """;
            cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
            recebidoFiado = ProductPriceHelper.RoundPrice(Convert.ToDouble(cmd.ExecuteScalar() ?? 0));
        }

        var lucro = ProductPriceHelper.RoundPrice(totalFaturado - cmv);
        var margem = totalFaturado > 0.009
            ? ProductPriceHelper.RoundPrice(lucro / totalFaturado * 100.0)
            : 0;

        return new FechamentoConsolidadoResult
        {
            DateFrom = dFrom,
            DateTo = dTo,
            QtdVendas = qtdVendas,
            TotalFaturado = totalFaturado,
            TotalAVista = totalAVista,
            TotalFiado = totalFiado,
            TotalRecebidoFiado = recebidoFiado,
            Cmv = cmv,
            CmvHistorico = periodCmv.Historical,
            CmvEstimado = periodCmv.EstimatedLegacy,
            HasEstimatedLegacyCost = periodCmv.HasEstimatedLegacyCost,
            CmvUsesHistoricalSnapshot = HistoricalSaleCostRules.ReportsUseHistoricalSnapshot,
            ProfitIsEstimated = periodCmv.ProfitIsEstimated,
            MarginIsEstimated = periodCmv.MarginIsEstimated,
            CmvReliabilityNote = periodCmv.ReliabilityNote,
            LucroEstimado = lucro,
            MargemPercent = margem,
            PorForma = porForma.ToDictionary(
                kv => kv.Key,
                kv => ProductPriceHelper.RoundPrice(kv.Value)),
        };
    }

    private static double SaleFiadoAmount(Microsoft.Data.Sqlite.SqliteConnection conn, int saleId, string paymentType)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(SUM(amount_in), 0) FROM cash_movements
            WHERE ref_type = 'sale' AND ref_id = $id AND kind = 'venda_fiado';
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        var fromCash = Convert.ToDouble(cmd.ExecuteScalar() ?? 0);
        if (fromCash > 0.009)
            return ProductPriceHelper.RoundPrice(fromCash);

        var low = (paymentType ?? "").ToLowerInvariant();
        if (low.Contains("fiado") || low.Contains("a prazo") || low == "prazo")
        {
            using var t = conn.CreateCommand();
            t.CommandText = "SELECT total FROM sales WHERE id = $id LIMIT 1;";
            t.Parameters.AddWithValue("$id", saleId);
            return ProductPriceHelper.RoundPrice(Convert.ToDouble(t.ExecuteScalar() ?? 0));
        }
        return 0;
    }

    private static string NormalizeForma(string? paymentType)
    {
        var s = (paymentType ?? "").Trim();
        if (string.IsNullOrEmpty(s))
            return "—";
        var low = s.ToLowerInvariant();
        if (low is "dinheiro" or "cash") return "Dinheiro";
        if (low == "pix") return "Pix";
        if (low.Contains("debito") || low.Contains("débito")) return "Cartão Débito";
        if (low.Contains("credito") || low.Contains("crédito")) return "Cartão Crédito";
        if (low.Contains("fiado")) return "Fiado";
        return s.Length > 40 ? s[..40] : s;
    }

    private static string FormatBrDateTime(string iso) =>
        DateBrHelper.FormatUtcToBrazil(iso, "dd/MM/yyyy HH:mm");

    private static string FormatPaymentLabel(string paymentType, double? cashReceived, double? changeAmount)
    {
        var label = string.IsNullOrWhiteSpace(paymentType) ? "—" : paymentType.Trim();
        if (cashReceived is > 0 && changeAmount is > 0.009)
            return $"{label} (troco R$ {changeAmount.Value:N2})";
        return label;
    }
}
