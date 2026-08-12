using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// DRE Simplificado: vendas (PDV + decks liquidados), CMV e despesas do Contas a Pagar.
/// Entregas ainda não existem no sistema — o faturamento cobre todas as vendas gravadas.
/// </summary>
public static class DreService
{
    public static DreSimplificadoResult GetDre(DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        var dFrom = (dateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var dTo = (dateTo ?? DateTime.Today).Date;
        if (dFrom > dTo)
            (dFrom, dTo) = (dTo, dFrom);

        using var conn = DatabaseService.OpenConnection();
        var fromStr = dFrom.ToString("yyyy-MM-dd");
        var toStr = dTo.ToString("yyyy-MM-dd");

        double receitaLiquida = 0;
        var qtdVendas = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT IFNULL(SUM(total),0), COUNT(*)
                FROM sales
                WHERE IFNULL(cancelled,0) = 0
                  AND session_date >= $from AND session_date <= $to;
                """;
            cmd.Parameters.AddWithValue("$from", fromStr);
            cmd.Parameters.AddWithValue("$to", toStr);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                receitaLiquida = Round(reader.GetDouble(0));
                qtdVendas = reader.GetInt32(1);
            }
        }

        double receitaBruta = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT IFNULL(SUM(si.subtotal),0)
                FROM sale_items si
                JOIN sales s ON s.id = si.sale_id
                WHERE IFNULL(s.cancelled,0) = 0
                  AND s.session_date >= $from AND s.session_date <= $to;
                """;
            cmd.Parameters.AddWithValue("$from", fromStr);
            cmd.Parameters.AddWithValue("$to", toStr);
            receitaBruta = Round(Convert.ToDouble(cmd.ExecuteScalar() ?? 0));
        }

        // Se não houver itens (legado), usa a própria líquida como bruta.
        if (receitaBruta < 0.009 && receitaLiquida > 0.009)
            receitaBruta = receitaLiquida;

        var descontos = Round(Math.Max(0, receitaBruta - receitaLiquida));

        double cancelamentos = 0;
        var qtdCanceladas = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT IFNULL(SUM(total),0), COUNT(*)
                FROM sales
                WHERE IFNULL(cancelled,0) = 1
                  AND session_date >= $from AND session_date <= $to;
                """;
            cmd.Parameters.AddWithValue("$from", fromStr);
            cmd.Parameters.AddWithValue("$to", toStr);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                cancelamentos = Round(reader.GetDouble(0));
                qtdCanceladas = reader.GetInt32(1);
            }
        }

        var deducoes = Round(descontos);

        var cmv = CalcCmv(conn, fromStr, toStr);
        var lucroBruto = Round(receitaLiquida - cmv);
        var margemBruta = receitaLiquida > 0.009
            ? Round(lucroBruto / receitaLiquida * 100.0)
            : 0;

        var (despesas, porCat) = CalcDespesasOperacionais(conn, fromStr, toStr);
        var lucroLiquido = Round(lucroBruto - despesas);
        var margemLiquida = receitaLiquida > 0.009
            ? Round(lucroLiquido / receitaLiquida * 100.0)
            : 0;

        var lines = BuildCascade(
            receitaBruta, descontos, cancelamentos, deducoes,
            receitaLiquida, cmv, lucroBruto, margemBruta,
            despesas, lucroLiquido, margemLiquida);

        return new DreSimplificadoResult
        {
            DateFrom = dFrom,
            DateTo = dTo,
            QtdVendas = qtdVendas,
            QtdCanceladas = qtdCanceladas,
            ReceitaBruta = receitaBruta,
            Descontos = descontos,
            Cancelamentos = cancelamentos,
            DeducoesVendas = deducoes,
            ReceitaLiquida = receitaLiquida,
            Cmv = cmv,
            LucroBruto = lucroBruto,
            MargemBrutaPercent = margemBruta,
            DespesasOperacionais = despesas,
            LucroLiquido = lucroLiquido,
            MargemLiquidaPercent = margemLiquida,
            DespesasPorCategoria = porCat,
            CascadeLines = lines,
        };
    }

    private static double CalcCmv(Microsoft.Data.Sqlite.SqliteConnection conn, string fromStr, string toStr)
    {
        double cmvSum = 0;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT si.quantity, si.unit_price, IFNULL(p.cost_price,0),
                   IFNULL(si.product_name,''), IFNULL(p.extra_json,''), IFNULL(p.group_name,'')
            FROM sale_items si
            JOIN sales s ON s.id = si.sale_id
            LEFT JOIN products p ON p.id = si.product_id
            WHERE IFNULL(s.cancelled,0) = 0
              AND s.session_date >= $from AND s.session_date <= $to;
            """;
        cmd.Parameters.AddWithValue("$from", fromStr);
        cmd.Parameters.AddWithValue("$to", toStr);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var qty = reader.GetDouble(0);
            var unitSale = reader.GetDouble(1);
            var catalogCost = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
            var name = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var extra = ProductExtra.Parse(reader.IsDBNull(4) ? null : reader.GetString(4));
            var group = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var unitCost = ProductPriceHelper.UnitCostForSoldLine(
                catalogCost, unitSale, extra, name, group);
            cmvSum += qty * unitCost;
        }
        return Round(cmvSum);
    }

    /// <summary>
    /// Despesas por vencimento no período, excluindo mercadoria (já coberta pelo CMV).
    /// </summary>
    private static (double Total, List<DreExpenseBreakdownRow> ByCat) CalcDespesasOperacionais(
        Microsoft.Data.Sqlite.SqliteConnection conn, string fromStr, string toStr)
    {
        var byCat = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        try
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
            cmd.Parameters.AddWithValue("$from", fromStr);
            cmd.Parameters.AddWithValue("$to", toStr);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var rawCat = reader.IsDBNull(0) ? "" : reader.GetString(0);
                if (IsMercadoriaCategory(rawCat))
                    continue;

                var cat = ResolveCategory(
                    rawCat,
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? "" : reader.GetString(2),
                    reader.IsDBNull(3) ? "" : reader.GetString(3),
                    reader.GetInt32(4));
                var amt = Round(reader.GetDouble(5));
                byCat[cat] = Round(byCat.GetValueOrDefault(cat) + amt);
            }
        }
        catch
        {
            // tabela pode não existir em bases antigas
        }

        var rows = byCat
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select(kv => new DreExpenseBreakdownRow { Category = kv.Key, Amount = kv.Value })
            .ToList();
        return (Round(rows.Sum(r => r.Amount)), rows);
    }

    private static bool IsMercadoriaCategory(string? category)
    {
        var c = (category ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(c))
            return false;
        if (c is "MERCADORIA" or "COMPRA" or "COMPRAS")
            return true;
        if (c.Contains("MERCADORIA", StringComparison.Ordinal))
            return true;
        if (c.StartsWith("00-", StringComparison.Ordinal))
            return true;
        return false;
    }

    private static string ResolveCategory(
        string expenseCategory, string supplierName, string notes, string number, int titleId)
    {
        var cat = (expenseCategory ?? "").Trim();
        if (!string.IsNullOrEmpty(cat))
            return Trunc(cat, 48);
        var sup = (supplierName ?? "").Trim();
        if (!string.IsNullOrEmpty(sup))
            return Trunc(sup, 48);
        var note = (notes ?? "").Trim();
        if (!string.IsNullOrEmpty(note))
            return Trunc(note, 48);
        var num = (number ?? "").Trim();
        return Trunc($"Título {(string.IsNullOrEmpty(num) ? titleId.ToString() : num)}", 48);
    }

    private static List<DreLineRow> BuildCascade(
        double bruta, double descontos, double cancelamentos, double deducoes,
        double liquida, double cmv, double lucroBruto, double margemBruta,
        double despesas, double lucroLiquido, double margemLiquida)
    {
        return
        [
            new DreLineRow { Sign = "(+)", Label = "Receita Bruta de Vendas", Amount = bruta },
            new DreLineRow
            {
                Sign = "(-)",
                Label = "Devoluções / Cancelamentos / Descontos",
                Amount = deducoes,
            },
            new DreLineRow
            {
                Sign = "",
                Label = $"    · Descontos concedidos: {ProductPriceHelper.MoneyBr(descontos)}",
                Amount = descontos,
                IsSubNote = true,
            },
            new DreLineRow
            {
                Sign = "",
                Label = cancelamentos > 0.009
                    ? $"    · Cancelamentos no período (fora da receita): {ProductPriceHelper.MoneyBr(cancelamentos)}"
                    : "    · Cancelamentos no período: R$ 0,00",
                Amount = cancelamentos,
                IsSubNote = true,
            },
            new DreLineRow
            {
                Sign = "(=)",
                Label = "Receita Líquida",
                Amount = liquida,
                IsTotal = true,
            },
            new DreLineRow { Sign = "(-)", Label = "Custo das Mercadorias Vendidas (CMV)", Amount = cmv },
            new DreLineRow
            {
                Sign = "(=)",
                Label = "Lucro Bruto",
                Amount = lucroBruto,
                IsTotal = true,
            },
            new DreLineRow
            {
                Sign = "",
                Label = $"    ↳ Margem Bruta {margemBruta:N1}%",
                Amount = margemBruta,
                IsSubNote = true,
            },
            new DreLineRow
            {
                Sign = "(-)",
                Label = "Despesas Operacionais (Contas a Pagar*)",
                Amount = despesas,
            },
            new DreLineRow
            {
                Sign = "(=)",
                Label = lucroLiquido >= 0 ? "Lucro Líquido do Exercício" : "Prejuízo do Exercício",
                Amount = lucroLiquido,
                IsTotal = true,
            },
            new DreLineRow
            {
                Sign = "",
                Label = $"    ↳ Margem Líquida {margemLiquida:N1}%",
                Amount = margemLiquida,
                IsSubNote = true,
            },
        ];
    }

    private static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..max];

    private static double Round(double v) => ProductPriceHelper.RoundPrice(v);
}
