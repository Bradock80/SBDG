using System.Globalization;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public static class BusinessDashboardService
{
    private static readonly string[] MonthsPt =
        ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    private static readonly string[] PieColors =
    [
        "#2563eb", "#eab308", "#ef4444", "#22c55e",
        "#1e3a8a", "#a855f7", "#f97316", "#64748b",
    ];

    public static NegocioDashboard GetDashboard(
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        string dateMode = "session",
        string rdDateMode = "due")
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.GetDashboard(dateFrom, dateTo, dateMode, rdDateMode);
        return GetDashboardLocal(dateFrom, dateTo, dateMode, rdDateMode);
    }

    public static NegocioDashboard GetDashboardLocal(
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        string dateMode = "session",
        string rdDateMode = "due")
    {
        var dTo = (dateTo ?? DateTime.Today).Date;
        var dFrom = (dateFrom ?? dTo.AddDays(-29)).Date;
        if (dFrom > dTo)
            (dFrom, dTo) = (dTo, dFrom);

        var mode = string.Equals(dateMode, "created", StringComparison.OrdinalIgnoreCase)
            ? "created"
            : "session";
        var rdMode = string.Equals(rdDateMode, "emission", StringComparison.OrdinalIgnoreCase)
            ? "emission"
            : "due";

        using var conn = DatabaseService.OpenConnection();

        var salesOk = new List<SaleRow>();
        var salesCancel = 0;
        LoadSales(conn, dFrom, dTo, mode, salesOk, ref salesCancel);

        var faturamento = Round(salesOk.Sum(s => s.Total));
        var qtdPedidos = salesOk.Count;
        var ticket = qtdPedidos > 0 ? Round(faturamento / qtdPedidos) : 0;

        var costCache = new Dictionary<int, double>();
        double itens = 0, cmv = 0;
        var topAgg = new Dictionary<int, (string Code, string Name, double Qty, double Total)>();

        foreach (var sale in salesOk)
        {
            foreach (var item in LoadSaleItems(conn, sale.Id))
            {
                itens += item.Qty;
                if (!costCache.TryGetValue(item.ProductId, out var cost))
                {
                    cost = GetProductCost(conn, item.ProductId);
                    costCache[item.ProductId] = cost;
                }
                cmv += item.Qty * cost;

                if (!topAgg.TryGetValue(item.ProductId, out var t))
                    t = (item.Code, item.Name, 0, 0);
                topAgg[item.ProductId] = (
                    t.Code,
                    t.Name,
                    Round3(t.Qty + item.Qty),
                    Round(t.Total + item.Subtotal));
            }
        }

        itens = Round3(itens);
        cmv = Round(cmv);
        var mediaItens = qtdPedidos > 0 ? Round(itens / qtdPedidos) : 0;
        var clientes = salesOk.Where(s => s.CustomerId is > 0).Select(s => s.CustomerId!.Value).Distinct().Count();

        var movs = LoadCashMovements(conn, dFrom, dTo);
        var despesas = Round(movs.Where(m => m.Kind is "sangria" or "compra").Sum(m => m.AmountOut));
        var recebFiado = Round(movs.Where(m => m.Kind == "recebimento_fiado").Sum(m => m.AmountIn));
        var suprimentos = Round(movs.Where(m => m.Kind == "suprimento").Sum(m => m.AmountIn));
        var receitasExtra = Round(recebFiado + suprimentos);
        var saldo = Round(faturamento + receitasExtra - despesas);
        var lucroBruto = Round(faturamento - cmv);
        var margemBruta = faturamento > 0.009 ? Round(lucroBruto / faturamento * 100) : 0;

        var daily = BuildDailyChart(salesOk, dFrom, dTo, mode);
        var top = BuildTop(topAgg);
        var insight = BuildVendasInsight(salesOk);
        var mensal = BuildMensal(conn, salesOk, dFrom, dTo, costCache);
        var margem = BuildMargemSaude(conn, mode, faturamento, cmv, qtdPedidos);

        var linhas = IterLinhasRecebimento(conn, salesOk, dFrom, dTo);
        var recebimentos = BuildRecebimentos(conn, linhas, faturamento, dFrom, dTo);
        var receitasDespesas = BuildReceitasDespesas(conn, dFrom, dTo, linhas, suprimentos, movs, rdMode);
        var taxas = BuildTaxasLucro(conn, linhas);

        return new NegocioDashboard
        {
            DateFrom = dFrom,
            DateTo = dTo,
            DateMode = mode,
            Faturamento = faturamento,
            QtdPedidos = qtdPedidos,
            TicketMedio = ticket,
            QtdCancelados = salesCancel,
            Cmv = cmv,
            ItensVendidos = itens,
            MediaItensPedido = mediaItens,
            ClientesAtendidos = clientes,
            Despesas = despesas,
            RecebimentosFiado = recebFiado,
            SaldoPeriodo = saldo,
            LucroBruto = lucroBruto,
            MargemBrutaPercent = margemBruta,
            DailyChart = daily,
            TopVendidos = top,
            VendasInsight = insight,
            MensalRows = mensal.Rows,
            MensalFaturamento = mensal.Fat,
            MensalCusto = mensal.Custo,
            MensalLucro = mensal.Lucro,
            MediaCatalogo = margem.MediaCatalogo,
            MargemVendasPeriodo = margem.MargemVendasPeriodo,
            MargemVendasHistorico = margem.MargemVendasHistorico,
            QtdVendasPeriodo = margem.QtdVendasPeriodo,
            QtdVendasHistorico = margem.QtdVendasHistorico,
            HistoricoFromBr = margem.HistoricoFromBr,
            HistoricoToBr = margem.HistoricoToBr,
            StatusLabel = margem.StatusLabel,
            StatusKey = margem.StatusKey,
            FaixaCritico = margem.Critico,
            FaixaAtencao = margem.Atencao,
            FaixaSaudavel = margem.Saudavel,
            FaixaExcelente = margem.Excelente,
            TotalComPreco = margem.TotalComPreco,
            Abaixo15 = margem.Abaixo15,
            MargemGrupos = margem.Grupos,
            MargemBenchmarks = margem.Benchmarks,
            MargemFaixasPie = margem.FaixasPie,
            Recebimentos = recebimentos,
            ReceitasDespesas = receitasDespesas,
            TaxasLucro = taxas,
        };
    }

    private sealed class SaleRow
    {
        public int Id { get; init; }
        public double Total { get; init; }
        public string PaymentType { get; init; } = "";
        public DateTime Day { get; init; }
        public int? CustomerId { get; init; }
    }

    private sealed record ItemRow(int ProductId, string Code, string Name, double Qty, double Subtotal);

    private sealed record RecebimentoLine(DateTime Day, double Amount, string PaymentType);

    private sealed record CashMovRow(DateTime Day, string Kind, double AmountIn, double AmountOut);

    private static void LoadSales(
        SqliteConnection conn, DateTime dFrom, DateTime dTo, string mode,
        List<SaleRow> salesOk, ref int cancelCount)
    {
        using var cmd = conn.CreateCommand();
        if (mode == "created")
        {
            cmd.CommandText = """
                SELECT id, total, IFNULL(payment_type,''), created_at, customer_id, cancelled
                FROM sales
                WHERE date(created_at) >= $from AND date(created_at) <= $to
                ORDER BY created_at;
                """;
        }
        else
        {
            cmd.CommandText = """
                SELECT id, total, IFNULL(payment_type,''), session_date, customer_id, cancelled
                FROM sales
                WHERE session_date >= $from AND session_date <= $to
                ORDER BY session_date, id;
                """;
        }
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var cancelled = reader.GetInt32(5) != 0;
            if (cancelled)
            {
                cancelCount++;
                continue;
            }

            var dayRaw = reader.GetString(3);
            DateTime day;
            if (mode == "created")
            {
                day = DateTime.TryParse(dayRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)
                    ? dt.Date
                    : dFrom;
            }
            else
            {
                day = DateTime.TryParse(dayRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                    ? dt.Date
                    : dFrom;
            }

            salesOk.Add(new SaleRow
            {
                Id = reader.GetInt32(0),
                Total = reader.GetDouble(1),
                PaymentType = reader.GetString(2),
                Day = day,
                CustomerId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            });
        }
    }

    private static List<ItemRow> LoadSaleItems(SqliteConnection conn, int saleId)
    {
        var list = new List<ItemRow>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT product_id, IFNULL(product_code,''), IFNULL(product_name,''),
                   quantity, subtotal
            FROM sale_items WHERE sale_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ItemRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetDouble(4)));
        }
        return list;
    }

    private static double GetProductCost(SqliteConnection conn, int productId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(cost_price,0) FROM products WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", productId);
        var obj = cmd.ExecuteScalar();
        return obj is null or DBNull ? 0 : Convert.ToDouble(obj);
    }

    private static List<CashMovRow> LoadCashMovements(SqliteConnection conn, DateTime dFrom, DateTime dTo)
    {
        var list = new List<CashMovRow>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT movement_date, kind, IFNULL(amount_in,0), IFNULL(amount_out,0)
            FROM cash_movements
            WHERE movement_date >= $from AND movement_date <= $to
              AND IFNULL(affects_balance,1) = 1;
            """;
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var dayRaw = reader.GetString(0);
            var day = DateTime.TryParse(dayRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt.Date
                : dFrom;
            list.Add(new CashMovRow(
                day,
                reader.GetString(1).ToLowerInvariant(),
                reader.GetDouble(2),
                reader.GetDouble(3)));
        }
        return list;
    }

    private static List<RecebimentoLine> IterLinhasRecebimento(
        SqliteConnection conn, List<SaleRow> salesOk, DateTime dFrom, DateTime dTo)
    {
        var linhas = new List<RecebimentoLine>();
        foreach (var sale in salesOk)
        {
            if (sale.Day < dFrom || sale.Day > dTo)
                continue;

            // Partes reais da venda (quebra DIN+PIX / DIN+DEB em formas puras)
            var parts = LoadSalePaymentParts(conn, sale.Id, sale.PaymentType, sale.Total);
            foreach (var part in parts)
            {
                if (IsFiado(part.PaymentType))
                    continue;
                linhas.Add(new RecebimentoLine(sale.Day, part.Amount, part.PaymentType));
            }
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT payment_date, IFNULL(amount,0), IFNULL(payment_type,'')
            FROM fiado_payments
            WHERE payment_date >= $from AND payment_date <= $to
              AND IFNULL(reversed,0) = 0;
            """;
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
        try
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var dayRaw = reader.GetString(0);
                var day = DateTime.TryParse(dayRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                    ? dt.Date
                    : dFrom;
                linhas.Add(new RecebimentoLine(day, reader.GetDouble(1), reader.GetString(2)));
            }
        }
        catch
        {
            // tabela pode não existir em DBs antigos
        }

        return linhas;
    }

    private sealed record PaymentPart(string PaymentType, double Amount);

    private static List<PaymentPart> LoadSalePaymentParts(
        SqliteConnection conn, int saleId, string paymentTypeLabel, double saleTotal)
    {
        var list = new List<PaymentPart>();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT IFNULL(payment_type,''), IFNULL(amount_in,0)
                FROM cash_movements
                WHERE ref_type = 'sale' AND ref_id = $id
                  AND kind IN ('venda', 'venda_fiado')
                ORDER BY id;
                """;
            cmd.Parameters.AddWithValue("$id", saleId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var amt = reader.GetDouble(1);
                if (amt <= 0.009)
                    continue;
                list.Add(new PaymentPart(reader.GetString(0), amt));
            }
        }
        catch
        {
            // ignore
        }

        if (list.Count > 0)
            return list;

        // Sem movimentos: tenta expandir DIN+PIX / DIN+DEB em partes iguais
        var expanded = ExpandCombinedPaymentLabel(paymentTypeLabel, saleTotal);
        if (expanded.Count > 0)
            return expanded;

        if (saleTotal > 0.009 && !IsFiado(paymentTypeLabel))
            list.Add(new PaymentPart(paymentTypeLabel, saleTotal));
        return list;
    }

    /// <summary>
    /// Quebra rótulos abreviados do PDV (ex.: DIN+PIX) em formas puras.
    /// Sem valores individuais, divide o total igualmente — só fallback sem cash_movements.
    /// </summary>
    private static List<PaymentPart> ExpandCombinedPaymentLabel(string paymentTypeLabel, double saleTotal)
    {
        var result = new List<PaymentPart>();
        if (saleTotal <= 0.009 || string.IsNullOrWhiteSpace(paymentTypeLabel))
            return result;
        if (!paymentTypeLabel.Contains('+', StringComparison.Ordinal))
            return result;

        var forms = new List<string>();
        foreach (var token in paymentTypeLabel.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var forma = MapPaymentAbbrev(token);
            if (string.IsNullOrEmpty(forma) || forma == "—" || IsFiado(forma))
                continue;
            forms.Add(forma);
        }
        if (forms.Count < 2)
            return result;

        var each = Round(saleTotal / forms.Count);
        var allocated = 0.0;
        for (var i = 0; i < forms.Count; i++)
        {
            var amt = i == forms.Count - 1 ? Round(saleTotal - allocated) : each;
            allocated = Round(allocated + amt);
            if (amt > 0.009)
                result.Add(new PaymentPart(forms[i], amt));
        }
        return result;
    }

    private static string? MapPaymentAbbrev(string token)
    {
        var t = token.Trim();
        if (string.IsNullOrEmpty(t))
            return null;
        var low = t.ToLowerInvariant()
            .Replace("é", "e", StringComparison.Ordinal)
            .Replace("á", "a", StringComparison.Ordinal);
        return low switch
        {
            "din" or "dinheiro" or "cash" => "Dinheiro",
            "pix" => "Pix",
            "deb" or "debito" => "Cartão Débito",
            "cred" or "credito" => "Cartão Crédito",
            "fiado" => "Fiado",
            "cheque" => "Cheque",
            "boleto" => "Boleto",
            _ => NormalizeFormaPagto(t),
        };
    }

    private static NegocioRecebimentosData BuildRecebimentos(
        SqliteConnection conn,
        List<RecebimentoLine> linhas,
        double faturamento,
        DateTime dFrom,
        DateTime dTo)
    {
        return new NegocioRecebimentosData
        {
            Faturamento = faturamento,
            TotalServicos = 0,
            TotalDescontos = 0,
            FormaSlices = RecebimentosPorForma(linhas),
            BandeiraSlices = RecebimentosPorBandeira(linhas),
            VsPagar = RecebimentosVsPagar(conn, linhas, dFrom, dTo),
        };
    }

    private static List<NegocioSliceRow> RecebimentosPorForma(List<RecebimentoLine> linhas)
    {
        var by = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in linhas)
        {
            var forma = NormalizeFormaPagto(line.PaymentType);
            if (forma == "—")
                forma = "Dinheiro";
            by[forma] = Round(by.GetValueOrDefault(forma) + line.Amount);
        }
        return SliceDictToChart(by);
    }

    private static List<NegocioSliceRow> RecebimentosPorBandeira(List<RecebimentoLine> linhas)
    {
        var by = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in linhas)
        {
            var label = BandeiraLabel(line.PaymentType);
            by[label] = Round(by.GetValueOrDefault(label) + line.Amount);
        }
        return SliceDictToChart(by);
    }

    private static string BandeiraLabel(string? raw)
    {
        var s = (raw ?? "").ToLowerInvariant();
        if (s.Contains("master")) return "Mastercard";
        if (s.Contains("visa")) return "Visa";
        if (s.Contains("elo")) return "Elo";
        if (s.Contains("amex") || s.Contains("american")) return "American Express";
        if (s.Contains("hiper")) return "Hipercard";
        var forma = NormalizeFormaPagto(raw);
        if (forma is "Cartão Débito" or "Cartão Crédito")
            return "Cartão (sem bandeira)";
        if (forma == "Pix") return "Pix";
        if (forma == "Dinheiro") return "Dinheiro";
        if (forma is "Fiado" or "—") return "Outros";
        return forma;
    }

    private static List<NegocioMonthCompareRow> RecebimentosVsPagar(
        SqliteConnection conn, List<RecebimentoLine> linhas, DateTime dFrom, DateTime dTo)
    {
        var months = MonthsInRange(dFrom, dTo);
        var rec = months.ToDictionary(m => m, _ => 0.0);
        var pag = months.ToDictionary(m => m, _ => 0.0);

        foreach (var line in linhas)
        {
            var mk = MonthKey(line.Day);
            if (rec.ContainsKey(mk))
                rec[mk] = Round(rec[mk] + line.Amount);
        }

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT due_date, IFNULL(amount,0)
                FROM payable_installments
                WHERE due_date >= $from AND due_date <= $to;
                """;
            cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var dayRaw = reader.GetString(0);
                if (!DateTime.TryParse(dayRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    continue;
                var mk = MonthKey(dt.Date);
                if (pag.ContainsKey(mk))
                    pag[mk] = Round(pag[mk] + reader.GetDouble(1));
            }
        }
        catch
        {
            // tabela pode não existir
        }

        return BuildCompareRows(months, rec, pag);
    }

    private static NegocioReceitasDespesasData BuildReceitasDespesas(
        SqliteConnection conn,
        DateTime dFrom,
        DateTime dTo,
        List<RecebimentoLine> linhas,
        double suprimentos,
        List<CashMovRow> movs,
        string rdMode)
    {
        var receitas = Round(linhas.Sum(l => l.Amount));
        var transferencia = Round(suprimentos);
        var receitasTotal = Round(receitas + transferencia);

        var byCat = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (rdMode == "emission")
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT IFNULL(t.expense_category,''), IFNULL(p.name,''), IFNULL(t.notes,''),
                           t.number, t.id, IFNULL(pi.amount,0)
                    FROM payable_titles t
                    LEFT JOIN people p ON p.id = t.supplier_id
                    LEFT JOIN payable_installments pi ON pi.title_id = t.id
                    WHERE t.emission_date >= $from AND t.emission_date <= $to;
                    """;
                cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var cat = PayableCategoria(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.IsDBNull(3) ? "" : reader.GetString(3),
                        reader.GetInt32(4));
                    byCat[cat] = Round(byCat.GetValueOrDefault(cat) + reader.GetDouble(5));
                }
            }
            else
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT IFNULL(t.expense_category,''), IFNULL(p.name,''), IFNULL(t.notes,''),
                           t.number, t.id, IFNULL(pi.amount,0)
                    FROM payable_installments pi
                    JOIN payable_titles t ON t.id = pi.title_id
                    LEFT JOIN people p ON p.id = t.supplier_id
                    WHERE pi.due_date >= $from AND pi.due_date <= $to;
                    """;
                cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var cat = PayableCategoria(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.IsDBNull(3) ? "" : reader.GetString(3),
                        reader.GetInt32(4));
                    byCat[cat] = Round(byCat.GetValueOrDefault(cat) + reader.GetDouble(5));
                }
            }
        }
        catch
        {
            // payables podem não existir
        }

        var despesasPrevisto = Round(byCat.Values.Sum());
        var despesasCaixa = Round(movs.Where(m => m.Kind is "sangria" or "compra").Sum(m => m.AmountOut));
        if (despesasCaixa > 0.009)
        {
            const string caixaCat = "Caixa (sangrias e pagamentos)";
            byCat[caixaCat] = Round(byCat.GetValueOrDefault(caixaCat) + despesasCaixa);
        }

        var despesasTotal = Round(despesasPrevisto + despesasCaixa);
        var saldo = Round(receitasTotal - despesasTotal);

        return new NegocioReceitasDespesasData
        {
            Receitas = receitas,
            TransferenciaCredito = transferencia,
            ReceitasTotal = receitasTotal,
            Despesas = despesasTotal,
            DespesasPrevisto = despesasPrevisto,
            DespesasCaixa = despesasCaixa,
            Saldo = saldo,
            DespesasCategoria = SliceDictToChart(byCat),
            MensalReceitasDespesas = RdMensalReceitasDespesas(conn, dFrom, dTo, linhas, movs, rdMode),
            MensalPrevistoRealizado = RdMensalPrevistoRealizado(conn, dFrom, dTo, movs, rdMode),
            RdDateMode = rdMode,
        };
    }

    private static string PayableCategoria(
        string expenseCategory, string supplierName, string notes, string number, int titleId)
    {
        var cat = (expenseCategory ?? "").Trim();
        if (!string.IsNullOrEmpty(cat))
            return Trunc(cat, 45);
        var sup = (supplierName ?? "").Trim();
        if (!string.IsNullOrEmpty(sup))
            return Trunc(sup, 45);
        var note = (notes ?? "").Trim();
        if (!string.IsNullOrEmpty(note))
            return Trunc(note, 45);
        var num = (number ?? "").Trim();
        return Trunc($"Título {(string.IsNullOrEmpty(num) ? titleId.ToString() : num)}", 45);
    }

    private static List<NegocioMonthCompareRow> RdMensalReceitasDespesas(
        SqliteConnection conn,
        DateTime dFrom,
        DateTime dTo,
        List<RecebimentoLine> linhas,
        List<CashMovRow> movs,
        string rdMode)
    {
        var months = MonthsInRange(dFrom, dTo);
        var receitasM = months.ToDictionary(m => m, _ => 0.0);
        var despesasM = months.ToDictionary(m => m, _ => 0.0);

        foreach (var line in linhas)
        {
            var mk = MonthKey(line.Day);
            if (receitasM.ContainsKey(mk))
                receitasM[mk] = Round(receitasM[mk] + line.Amount);
        }

        foreach (var mov in movs)
        {
            var mk = MonthKey(mov.Day);
            if (!receitasM.ContainsKey(mk))
                continue;
            if (mov.Kind == "suprimento")
                receitasM[mk] = Round(receitasM[mk] + mov.AmountIn);
            if (mov.Kind is "sangria" or "compra")
                despesasM[mk] = Round(despesasM[mk] + mov.AmountOut);
        }

        try
        {
            if (rdMode == "emission")
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT t.emission_date, IFNULL(pi.amount,0)
                    FROM payable_titles t
                    JOIN payable_installments pi ON pi.title_id = t.id
                    WHERE t.emission_date >= $from AND t.emission_date <= $to;
                    """;
                cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (!DateTime.TryParse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        continue;
                    var mk = MonthKey(dt.Date);
                    if (despesasM.ContainsKey(mk))
                        despesasM[mk] = Round(despesasM[mk] + reader.GetDouble(1));
                }
            }
            else
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT due_date, IFNULL(amount,0)
                    FROM payable_installments
                    WHERE due_date >= $from AND due_date <= $to;
                    """;
                cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (!DateTime.TryParse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        continue;
                    var mk = MonthKey(dt.Date);
                    if (despesasM.ContainsKey(mk))
                        despesasM[mk] = Round(despesasM[mk] + reader.GetDouble(1));
                }
            }
        }
        catch
        {
            // ignore
        }

        return BuildCompareRows(months, receitasM, despesasM);
    }

    private static List<NegocioMonthCompareRow> RdMensalPrevistoRealizado(
        SqliteConnection conn,
        DateTime dFrom,
        DateTime dTo,
        List<CashMovRow> movs,
        string rdMode)
    {
        var months = MonthsInRange(dFrom, dTo);
        var previstoM = months.ToDictionary(m => m, _ => 0.0);
        var realizadoM = months.ToDictionary(m => m, _ => 0.0);

        try
        {
            if (rdMode == "emission")
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT t.emission_date, IFNULL(pi.amount,0)
                    FROM payable_titles t
                    JOIN payable_installments pi ON pi.title_id = t.id
                    WHERE t.emission_date >= $from AND t.emission_date <= $to;
                    """;
                cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (!DateTime.TryParse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        continue;
                    var mk = MonthKey(dt.Date);
                    if (previstoM.ContainsKey(mk))
                        previstoM[mk] = Round(previstoM[mk] + reader.GetDouble(1));
                }
            }
            else
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT due_date, IFNULL(amount,0)
                    FROM payable_installments
                    WHERE due_date >= $from AND due_date <= $to;
                    """;
                cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (!DateTime.TryParse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        continue;
                    var mk = MonthKey(dt.Date);
                    if (previstoM.ContainsKey(mk))
                        previstoM[mk] = Round(previstoM[mk] + reader.GetDouble(1));
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT paid_date, IFNULL(paid_amount,0), IFNULL(amount,0), IFNULL(status,'')
                    FROM payable_installments
                    WHERE paid_date IS NOT NULL
                      AND paid_date >= $from AND paid_date <= $to;
                    """;
                cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (!DateTime.TryParse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        continue;
                    var mk = MonthKey(dt.Date);
                    if (!realizadoM.ContainsKey(mk))
                        continue;
                    var paid = reader.GetDouble(1);
                    if (paid <= 0.009 && string.Equals(reader.GetString(3), "pago", StringComparison.OrdinalIgnoreCase))
                        paid = reader.GetDouble(2);
                    realizadoM[mk] = Round(realizadoM[mk] + paid);
                }
            }
        }
        catch
        {
            // ignore
        }

        foreach (var mov in movs)
        {
            if (mov.Kind is not ("sangria" or "compra"))
                continue;
            var mk = MonthKey(mov.Day);
            if (realizadoM.ContainsKey(mk))
                realizadoM[mk] = Round(realizadoM[mk] + mov.AmountOut);
        }

        return BuildCompareRows(months, previstoM, realizadoM);
    }

    private static NegocioTaxasLucroData BuildTaxasLucro(SqliteConnection conn, List<RecebimentoLine> linhas)
    {
        var feeMap = LoadFeeInfoByApiLabel(conn);
        var byFormaAmt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var byFormaTaxa = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in linhas)
        {
            if (line.Amount <= 0)
                continue;
            var forma = NormalizeFormaPagto(line.PaymentType);
            if (forma == "—")
                forma = "Dinheiro";
            feeMap.TryGetValue(forma, out var info);
            var pct = info?.FeePercent ?? 0;
            var taxaValor = PaymentMethodsService.CalcFeeAmount(line.Amount, pct, info?.FeeFixed ?? 0);
            byFormaAmt[forma] = Round(byFormaAmt.GetValueOrDefault(forma) + line.Amount);
            byFormaTaxa[forma] = Round(byFormaTaxa.GetValueOrDefault(forma) + taxaValor);
        }

        var totalTaxas = Round(byFormaTaxa.Values.Sum());
        var totalRecebido = Round(byFormaAmt.Values.Sum());
        var totalComTaxa = Round(byFormaAmt
            .Where(kv =>
            {
                feeMap.TryGetValue(kv.Key, out var i);
                return (i?.FeePercent ?? 0) > 0.0001 || (i?.FeeFixed ?? 0) > 0.009;
            })
            .Sum(kv => kv.Value));
        var liquido = Round(totalRecebido - totalTaxas);
        var pctSobre = totalRecebido > 0.009 ? Round(totalTaxas / totalRecebido * 100) : 0;

        var detalhe = byFormaAmt
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select(kv =>
            {
                var vendido = kv.Value;
                var taxaValor = byFormaTaxa.GetValueOrDefault(kv.Key);
                feeMap.TryGetValue(kv.Key, out var info);
                var pct = info?.FeePercent ?? 0;
                return new NegocioTaxaDetalheRow
                {
                    Forma = kv.Key,
                    Vendido = vendido,
                    FeePercent = pct,
                    TaxaValor = taxaValor,
                    Liquido = Round(vendido - taxaValor),
                };
            })
            .ToList();

        var taxasPorForma = SliceDictToChart(
            byFormaTaxa.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value));

        var resumoSource = detalhe
            .Where(r => r.TaxaValor > 0.009 || r.FeePercent > 0.009)
            .ToList();
        var maxResumo = Math.Max(0.01, resumoSource.Count == 0
            ? 0.01
            : resumoSource.Max(r => Math.Max(r.Vendido, r.TaxaValor)));
        var resumoBarras = resumoSource.Select(r => new NegocioMonthCompareRow
        {
            Mes = r.Forma,
            SerieA = r.Vendido,
            SerieB = r.TaxaValor,
            HeightRatioA = r.Vendido / maxResumo,
            HeightRatioB = r.TaxaValor / maxResumo,
        }).ToList();

        return new NegocioTaxasLucroData
        {
            TotalTaxas = totalTaxas,
            TotalRecebido = totalRecebido,
            TotalComTaxa = totalComTaxa,
            LiquidoAposTaxas = liquido,
            PctSobreRecebido = pctSobre,
            TaxasPorForma = taxasPorForma,
            Detalhe = detalhe,
            ResumoBarras = resumoBarras,
        };
    }

    private static Dictionary<string, PaymentFeeInfo> LoadFeeInfoByApiLabel(SqliteConnection conn)
    {
        _ = conn;
        return PaymentMethodsService.FeeInfoByApiLabel();
    }

    private static Dictionary<string, double> LoadFeeMapByApiLabel(SqliteConnection conn)
    {
        _ = conn;
        return PaymentMethodsService.FeeMapByApiLabel();
    }

    private static List<NegocioSliceRow> SliceDictToChart(Dictionary<string, double> byKey)
    {
        var total = byKey.Values.Sum();
        var rows = byKey
            .Where(kv => kv.Value > 0.009)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select((kv, i) => new NegocioSliceRow
            {
                Label = kv.Key,
                Total = Round(kv.Value),
                Pct = total > 0.009 ? Round(100.0 * kv.Value / total) : 0,
                Color = PieColors[i % PieColors.Length],
            })
            .ToList();

        var max = Math.Max(0.01, rows.Count == 0 ? 0.01 : rows.Max(r => r.Total));
        foreach (var r in rows)
            r.BarRatio = r.Total / max;
        return rows;
    }

    private static List<NegocioMonthCompareRow> BuildCompareRows(
        List<string> months, Dictionary<string, double> serieA, Dictionary<string, double> serieB)
    {
        var rows = months.Select(m => new NegocioMonthCompareRow
        {
            Mes = MonthLabelBr(m),
            SerieA = serieA.GetValueOrDefault(m),
            SerieB = serieB.GetValueOrDefault(m),
        }).ToList();

        var max = Math.Max(0.01, rows.Count == 0
            ? 0.01
            : rows.Max(r => Math.Max(r.SerieA, r.SerieB)));
        foreach (var r in rows)
        {
            r.HeightRatioA = r.SerieA / max;
            r.HeightRatioB = r.SerieB / max;
        }
        return rows;
    }

    private static List<string> MonthsInRange(DateTime dFrom, DateTime dTo)
    {
        var keys = new List<string>();
        var cur = new DateTime(dFrom.Year, dFrom.Month, 1);
        var end = new DateTime(dTo.Year, dTo.Month, 1);
        while (cur <= end)
        {
            keys.Add($"{cur.Year:D4}-{cur.Month:D2}");
            cur = cur.AddMonths(1);
        }
        return keys;
    }

    private static string MonthKey(DateTime d) => $"{d.Year:D4}-{d.Month:D2}";

    private static string MonthLabelBr(string ym)
    {
        var parts = ym.Split('-');
        var year = int.Parse(parts[0]);
        var month = int.Parse(parts[1]);
        return $"{MonthsPt[month - 1]}/{year % 100:D2}";
    }

    private static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..max];

    private static readonly string[] DiasSemanaPt =
        ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];

    private static NegocioVendasInsight BuildVendasInsight(List<SaleRow> salesOk)
    {
        if (salesOk.Count == 0)
            return new NegocioVendasInsight();

        var byDow = new double[7];
        var byDay = new Dictionary<string, double>();
        double inicio = 0, meio = 0, fim = 0;

        foreach (var s in salesOk)
        {
            byDow[(int)s.Day.DayOfWeek] = Round(byDow[(int)s.Day.DayOfWeek] + s.Total);

            var key = s.Day.ToString("yyyy-MM-dd");
            byDay[key] = Round((byDay.TryGetValue(key, out var cur) ? cur : 0) + s.Total);

            var d = s.Day.Day;
            if (d <= 10) inicio = Round(inicio + s.Total);
            else if (d <= 20) meio = Round(meio + s.Total);
            else fim = Round(fim + s.Total);
        }

        var maxDow = byDow.Max();
        var melhorDowIdx = Array.IndexOf(byDow, maxDow);
        var diasRows = Enumerable.Range(0, 7)
            .Select(i => new NegocioDiaSemanaRow
            {
                Dia = DiasSemanaPt[i],
                Total = byDow[i],
                IsMelhor = byDow[i] > 0.009 && Math.Abs(byDow[i] - maxDow) < 0.001,
            })
            .OrderByDescending(r => r.Total)
            .ThenBy(r => r.Dia)
            .ToList();

        var maxBar = Math.Max(0.01, maxDow);
        foreach (var r in diasRows)
            r.BarRatio = r.Total / maxBar;

        var ranking = string.Join(" · ", diasRows.Where(r => r.Total > 0.009).Take(3).Select(r => r.Dia));

        var melhorPeriodo = "—";
        var maxPeriodo = Math.Max(inicio, Math.Max(meio, fim));
        if (maxPeriodo > 0.009)
        {
            if (Math.Abs(maxPeriodo - inicio) < 0.001) melhorPeriodo = "Início do mês";
            else if (Math.Abs(maxPeriodo - meio) < 0.001) melhorPeriodo = "Meio do mês";
            else melhorPeriodo = "Final do mês";
        }

        string melhorData = "—";
        double melhorDataTotal = 0;
        if (byDay.Count > 0)
        {
            var best = byDay.OrderByDescending(kv => kv.Value).First();
            melhorDataTotal = best.Value;
            if (DateTime.TryParse(best.Key, out var dt))
                melhorData = $"{dt:dd/MM} ({DiasSemanaPt[(int)dt.DayOfWeek]})";
            else
                melhorData = best.Key;
        }

        return new NegocioVendasInsight
        {
            TemDados = maxDow > 0.009,
            MelhorDiaSemana = maxDow > 0.009 ? DiasSemanaPt[melhorDowIdx] : "—",
            MelhorDiaSemanaTotal = maxDow,
            RankingDiasResumo = ranking,
            DiasSemana = diasRows,
            PeriodoMesMelhor = melhorPeriodo,
            InicioMesTotal = inicio,
            MeioMesTotal = meio,
            FimMesTotal = fim,
            MelhorDiaData = melhorData,
            MelhorDiaDataTotal = melhorDataTotal,
        };
    }

    private static List<NegocioChartPoint> BuildDailyChart(
        List<SaleRow> sales, DateTime dFrom, DateTime dTo, string mode)
    {
        var caixa = new Dictionary<string, double>();
        var fiado = new Dictionary<string, double>();
        for (var d = dFrom; d <= dTo; d = d.AddDays(1))
        {
            var key = d.ToString("yyyy-MM-dd");
            caixa[key] = 0;
            fiado[key] = 0;
        }

        foreach (var s in sales)
        {
            var key = s.Day.ToString("yyyy-MM-dd");
            if (!caixa.ContainsKey(key))
                continue;
            if (IsFiado(s.PaymentType))
                fiado[key] = Round(fiado[key] + s.Total);
            else
                caixa[key] = Round(caixa[key] + s.Total);
        }

        var points = new List<NegocioChartPoint>();
        double max = 0.01;
        for (var d = dFrom; d <= dTo; d = d.AddDays(1))
        {
            var key = d.ToString("yyyy-MM-dd");
            var p = new NegocioChartPoint
            {
                Label = d.ToString("dd/MM"),
                Caixa = caixa[key],
                Fiado = fiado[key],
            };
            if (p.Total > max) max = p.Total;
            points.Add(p);
        }

        foreach (var p in points)
        {
            p.HeightRatio = p.Total / max;
            p.CaixaRatio = p.Total > 0.009 ? p.Caixa / p.Total : 0;
            p.FiadoRatio = p.Total > 0.009 ? p.Fiado / p.Total : 0;
        }

        if (points.Count > 45)
            return AggregateWeekly(points, max);

        return points;
    }

    private static List<NegocioChartPoint> AggregateWeekly(List<NegocioChartPoint> daily, double _)
    {
        var weeks = new List<NegocioChartPoint>();
        for (var i = 0; i < daily.Count; i += 7)
        {
            var slice = daily.Skip(i).Take(7).ToList();
            var p = new NegocioChartPoint
            {
                Label = slice[0].Label,
                Caixa = Round(slice.Sum(x => x.Caixa)),
                Fiado = Round(slice.Sum(x => x.Fiado)),
            };
            weeks.Add(p);
        }
        var max = Math.Max(0.01, weeks.Max(w => w.Total));
        foreach (var p in weeks)
        {
            p.HeightRatio = p.Total / max;
            p.CaixaRatio = p.Total > 0.009 ? p.Caixa / p.Total : 0;
            p.FiadoRatio = p.Total > 0.009 ? p.Fiado / p.Total : 0;
        }
        return weeks;
    }

    private static List<NegocioTopRow> BuildTop(
        Dictionary<int, (string Code, string Name, double Qty, double Total)> agg)
    {
        var rows = agg.Values
            .OrderByDescending(x => x.Qty)
            .ThenByDescending(x => x.Total)
            .ThenBy(x => x.Name)
            .Take(15)
            .Select((x, i) => new NegocioTopRow
            {
                Posicao = i + 1,
                Code = x.Code,
                Name = x.Name,
                Qty = x.Qty,
                Total = x.Total,
            })
            .ToList();

        var max = Math.Max(0.01, rows.Count == 0 ? 0.01 : rows.Max(r => r.Qty));
        foreach (var r in rows)
            r.BarRatio = r.Qty / max;
        return rows;
    }

    private static (List<NegocioMensalRow> Rows, double Fat, double Custo, double Lucro) BuildMensal(
        SqliteConnection conn,
        List<SaleRow> salesOk,
        DateTime dFrom,
        DateTime dTo,
        Dictionary<int, double> costCache)
    {
        var map = new Dictionary<string, (double Fat, double Custo, double Sangria, double Pag)>();

        var cursor = new DateTime(dFrom.Year, dFrom.Month, 1);
        var end = new DateTime(dTo.Year, dTo.Month, 1);
        while (cursor <= end)
        {
            var key = $"{cursor.Year}-{cursor.Month:D2}";
            map[key] = (0, 0, 0, 0);
            cursor = cursor.AddMonths(1);
        }

        foreach (var sale in salesOk)
        {
            var key = $"{sale.Day.Year}-{sale.Day.Month:D2}";
            if (!map.ContainsKey(key))
                continue;
            var cur = map[key];
            double saleCmv = 0;
            foreach (var item in LoadSaleItems(conn, sale.Id))
            {
                if (!costCache.TryGetValue(item.ProductId, out var cost))
                {
                    cost = GetProductCost(conn, item.ProductId);
                    costCache[item.ProductId] = cost;
                }
                saleCmv += item.Qty * cost;
            }
            map[key] = (Round(cur.Fat + sale.Total), Round(cur.Custo + saleCmv), cur.Sangria, cur.Pag);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT substr(movement_date,1,7), COALESCE(SUM(amount_out),0)
                FROM cash_movements
                WHERE kind = 'sangria'
                  AND movement_date >= $from AND movement_date <= $to
                  AND IFNULL(affects_balance,1) = 1
                GROUP BY substr(movement_date,1,7);
                """;
            cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                if (!map.ContainsKey(key))
                    continue;
                var cur = map[key];
                map[key] = (cur.Fat, cur.Custo, Round(reader.GetDouble(1)), cur.Pag);
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT substr(IFNULL(paid_date, due_date),1,7), COALESCE(SUM(paid_amount),0)
                FROM payable_installments
                WHERE lower(status) = 'pago'
                  AND IFNULL(paid_date, due_date) >= $from
                  AND IFNULL(paid_date, due_date) <= $to
                GROUP BY substr(IFNULL(paid_date, due_date),1,7);
                """;
            cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));
            try
            {
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var key = reader.GetString(0);
                    if (!map.ContainsKey(key))
                        continue;
                    var cur = map[key];
                    map[key] = (cur.Fat, cur.Custo, cur.Sangria, Round(reader.GetDouble(1)));
                }
            }
            catch
            {
                // tabela pode não existir em DBs antigos
            }
        }

        var rows = new List<NegocioMensalRow>();
        double totFat = 0, totCusto = 0, totLucro = 0;
        foreach (var kv in map.OrderBy(k => k.Key))
        {
            var parts = kv.Key.Split('-');
            var year = int.Parse(parts[0]);
            var month = int.Parse(parts[1]);
            var gasto = Round(kv.Value.Custo + kv.Value.Sangria);
            var lucro = Round(kv.Value.Fat - gasto);
            totFat += kv.Value.Fat;
            totCusto += gasto;
            totLucro += lucro;
            rows.Add(new NegocioMensalRow
            {
                Mes = $"{MonthsPt[month - 1]}/{year % 100:D2}",
                Faturamento = kv.Value.Fat,
                Custo = gasto,
                Lucro = lucro,
                PagFornecedor = kv.Value.Pag,
            });
        }

        return (rows, Round(totFat), Round(totCusto), Round(totLucro));
    }

    private static readonly Dictionary<string, string> MargemFaixaColors = new(StringComparer.Ordinal)
    {
        ["Crítico (<15%)"] = "#ef4444",
        ["Atenção (15–18%)"] = "#f97316",
        ["Saudável (18–22%)"] = "#22c55e",
        ["Excelente (>22%)"] = "#2563eb",
    };

    private static (
        double MediaCatalogo,
        double MargemVendasPeriodo,
        double MargemVendasHistorico,
        int QtdVendasPeriodo,
        int QtdVendasHistorico,
        string HistoricoFromBr,
        string HistoricoToBr,
        string StatusLabel,
        string StatusKey,
        int Critico,
        int Atencao,
        int Saudavel,
        int Excelente,
        int TotalComPreco,
        List<NegocioMargemCriticoRow> Abaixo15,
        List<NegocioMargemGrupoRow> Grupos,
        List<NegocioMargemBenchmarkRow> Benchmarks,
        List<NegocioSliceRow> FaixasPie
    ) BuildMargemSaude(
        SqliteConnection conn,
        string mode,
        double faturamento,
        double cmv,
        int qtdVendasPeriodo)
    {
        var margemPeriodo = faturamento > 0.009 ? Round((faturamento - cmv) / faturamento * 100) : 0;
        var hist = AggregateSalesStats(conn, mode);
        var margemHist = hist.Faturamento > 0.009
            ? Round((hist.Faturamento - hist.Cmv) / hist.Faturamento * 100)
            : 0;

        var abaixo = new List<NegocioMargemCriticoRow>();
        var byGroup = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        int critico = 0, atencao = 0, saudavel = 0, excelente = 0, totalComPreco = 0;
        int mediaCount = 0;
        double sumMargin = 0;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT IFNULL(code,''), IFNULL(name,''), IFNULL(group_name,''),
                       IFNULL(cost_price,0), IFNULL(sale_price,0)
                FROM products
                WHERE IFNULL(active,1) = 1
                ORDER BY name;
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var cost = reader.GetDouble(3);
                var sale = reader.GetDouble(4);
                if (sale <= 0.009)
                    continue;
                totalComPreco++;
                var margin = ProductPriceHelper.MarginOnSale(cost, sale);
                // Margem &lt; -100% ou custo &gt; venda: provável custo de fardo no cadastro unitário
                var suspeito = margin < -100.0 || cost > sale + 0.009;

                if (!suspeito)
                {
                    mediaCount++;
                    sumMargin += margin;
                }

                var groupRaw = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var group = string.IsNullOrWhiteSpace(groupRaw) ? "Sem grupo" : groupRaw.Trim();
                if (!suspeito)
                {
                    if (!byGroup.TryGetValue(group, out var list))
                    {
                        list = [];
                        byGroup[group] = list;
                    }
                    list.Add(margin);
                }

                if (margin < 15) critico++;
                else if (margin < 18) atencao++;
                else if (margin <= 22) saudavel++;
                else excelente++;

                if (margin < 15)
                {
                    abaixo.Add(new NegocioMargemCriticoRow
                    {
                        Code = reader.GetString(0),
                        Name = reader.GetString(1),
                        GroupName = group,
                        CostPrice = Round(cost),
                        SalePrice = Round(sale),
                        MarginPercent = margin,
                        IsCadastroSuspeito = suspeito,
                    });
                }
            }
        }

        abaixo = abaixo.OrderBy(r => r.MarginPercent).ThenBy(r => r.Name).Take(50).ToList();
        var media = mediaCount > 0 ? Round(sumMargin / mediaCount) : 0;

        var faixasPie = SliceCountsToChart(new Dictionary<string, double>
        {
            ["Crítico (<15%)"] = critico,
            ["Atenção (15–18%)"] = atencao,
            ["Saudável (18–22%)"] = saudavel,
            ["Excelente (>22%)"] = excelente,
        }, MargemFaixaColors);

        const double grupoEixoMax = 50.0;
        var grupos = byGroup
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => new NegocioMargemGrupoRow
            {
                Label = kv.Key,
                MarginPercent = Round(kv.Value.Sum() / kv.Value.Count),
                Qty = kv.Value.Count,
            })
            .OrderByDescending(g => g.Qty)
            .ThenBy(g => g.Label)
            .Take(12)
            .ToList();
        foreach (var g in grupos)
            g.BarRatio = Math.Clamp(g.MarginPercent / grupoEixoMax, 0, 1);

        string statusLabel, statusKey;
        if (media >= 18) { statusLabel = "Saudável"; statusKey = "saudavel"; }
        else if (media >= 15) { statusLabel = "Atenção"; statusKey = "atencao"; }
        else { statusLabel = "Crítico"; statusKey = "critico"; }

        var benchmarks = new List<NegocioMargemBenchmarkRow>
        {
            new() { Label = "Mínimo mercado", Value = 15.0, Color = "#f97316" },
            new() { Label = "Sua média (catálogo)", Value = media, Color = "#2563eb" },
            new() { Label = "Meta ideal depósito", Value = 20.0, Color = "#22c55e" },
        };
        if (faturamento > 0.009)
            benchmarks.Add(new NegocioMargemBenchmarkRow
            {
                Label = "Margem vendas (período)",
                Value = margemPeriodo,
                Color = "#8b5cf6",
            });
        if (hist.Faturamento > 0.009)
            benchmarks.Add(new NegocioMargemBenchmarkRow
            {
                Label = "Margem vendas (histórico)",
                Value = margemHist,
                Color = "#a855f7",
            });
        var maxBench = Math.Max(25.0, benchmarks.Max(b => b.Value));
        foreach (var b in benchmarks)
            b.BarRatio = b.Value / maxBench;

        return (
            media,
            margemPeriodo,
            margemHist,
            qtdVendasPeriodo,
            hist.Qtd,
            hist.FromBr,
            hist.ToBr,
            statusLabel,
            statusKey,
            critico,
            atencao,
            saudavel,
            excelente,
            totalComPreco,
            abaixo,
            grupos,
            benchmarks,
            faixasPie
        );
    }

    private static (double Faturamento, double Cmv, int Qtd, string FromBr, string ToBr) AggregateSalesStats(
        SqliteConnection conn,
        string mode)
    {
        using var cmdFat = conn.CreateCommand();
        cmdFat.CommandText = """
            SELECT IFNULL(SUM(total),0), COUNT(*)
            FROM sales
            WHERE IFNULL(cancelled,0) = 0;
            """;
        double fat;
        int qtd;
        using (var reader = cmdFat.ExecuteReader())
        {
            reader.Read();
            fat = Round(reader.GetDouble(0));
            qtd = reader.GetInt32(1);
        }

        using var cmdCmv = conn.CreateCommand();
        cmdCmv.CommandText = """
            SELECT si.quantity, si.unit_price, IFNULL(p.cost_price,0),
                   IFNULL(si.product_name,''), IFNULL(p.extra_json,''), IFNULL(p.group_name,'')
            FROM sale_items si
            INNER JOIN sales s ON si.sale_id = s.id
            LEFT JOIN products p ON si.product_id = p.id
            WHERE IFNULL(s.cancelled,0) = 0;
            """;
        double cmvSum = 0;
        using (var cmvReader = cmdCmv.ExecuteReader())
        {
            while (cmvReader.Read())
            {
                var qty = cmvReader.GetDouble(0);
                var unitSale = cmvReader.GetDouble(1);
                var catalogCost = cmvReader.IsDBNull(2) ? 0 : cmvReader.GetDouble(2);
                var name = cmvReader.IsDBNull(3) ? "" : cmvReader.GetString(3);
                var extra = ProductExtra.Parse(cmvReader.IsDBNull(4) ? null : cmvReader.GetString(4));
                var group = cmvReader.IsDBNull(5) ? "" : cmvReader.GetString(5);
                var unitCost = ProductPriceHelper.UnitCostForSoldLine(
                    catalogCost, unitSale, extra, name, group);
                cmvSum += qty * unitCost;
            }
        }
        var cmv = Round(cmvSum);

        string fromBr = "—", toBr = "—";
        using var cmdRange = conn.CreateCommand();
        if (mode == "created")
        {
            cmdRange.CommandText = """
                SELECT MIN(created_at), MAX(created_at)
                FROM sales
                WHERE IFNULL(cancelled,0) = 0;
                """;
        }
        else
        {
            cmdRange.CommandText = """
                SELECT MIN(session_date), MAX(session_date)
                FROM sales
                WHERE IFNULL(cancelled,0) = 0;
                """;
        }

        using (var reader = cmdRange.ExecuteReader())
        {
            if (reader.Read() && !reader.IsDBNull(0) && !reader.IsDBNull(1))
            {
                fromBr = FormatDateBr(reader.GetString(0));
                toBr = FormatDateBr(reader.GetString(1));
            }
        }

        return (fat, cmv, qtd, fromBr, toBr);
    }

    private static string FormatDateBr(string raw)
    {
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            return dt.ToString("dd/MM/yyyy");
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return dt.ToString("dd/MM/yyyy");
        return "—";
    }

    private static List<NegocioSliceRow> SliceCountsToChart(
        Dictionary<string, double> byKey,
        Dictionary<string, string>? colorMap = null)
    {
        var total = byKey.Values.Sum();
        var rows = byKey
            .Where(kv => kv.Value > 0.009)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select((kv, i) =>
            {
                var pct = total > 0.009 ? Round(100.0 * kv.Value / total) : 0;
                var color = colorMap != null && colorMap.TryGetValue(kv.Key, out var mapped)
                    ? mapped
                    : PieColors[i % PieColors.Length];
                return new NegocioSliceRow
                {
                    Label = kv.Key,
                    Total = kv.Value,
                    Pct = pct,
                    Color = color,
                    BarRatio = total > 0.009 ? kv.Value / total : 0,
                };
            })
            .ToList();
        return rows;
    }

    private static string NormalizeFormaPagto(string? paymentType)
    {
        var s = (paymentType ?? "").Trim();
        if (string.IsNullOrEmpty(s))
            return "—";
        // Rótulo combinado (DIN+PIX) — só aparece se o split falhou
        if (s.Contains('+', StringComparison.Ordinal))
            return s.Length > 30 ? s[..30] : s;

        var low = s.ToLowerInvariant()
            .Replace("é", "e", StringComparison.Ordinal)
            .Replace("á", "a", StringComparison.Ordinal)
            .Replace("ã", "a", StringComparison.Ordinal);

        if (low is "dinheiro" or "cash" or "din") return "Dinheiro";
        if (low == "pix") return "Pix";
        if (low == "deb" || low.Contains("debito"))
            return "Cartão Débito";
        if (low == "cred" || low.Contains("credito"))
            return "Cartão Crédito";
        if (low == "cheque") return "Cheque";
        if (low == "boleto") return "Boleto";
        if (low == "transferencia") return "Transferência";
        if (low is "fiado" or "a prazo" or "prazo") return "Fiado";
        return s.Length > 30 ? s[..30] : s;
    }


    private static bool IsFiado(string? paymentType)
    {
        var low = (paymentType ?? "").Trim().ToLowerInvariant();
        return low.Contains("fiado") || low.Contains("a prazo") || low is "prazo";
    }

    private static double Round(double v) => ProductPriceHelper.RoundPrice(v);
    private static double Round3(double v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);
}
