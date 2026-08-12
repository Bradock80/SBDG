using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Relatório de Entradas e Saídas do estoque.
/// Prioriza a tabela movements; completa com vendas/compras históricas
/// que ainda não foram gravadas em movements.
/// </summary>
public static class StockIoService
{
    public static StockIoReportResult List(
        DateTime dateFrom,
        DateTime dateTo,
        StockIoDirectionFilter direction = StockIoDirectionFilter.Todas,
        string? search = null,
        int limit = 2000)
    {
        var dFrom = dateFrom.Date;
        var dTo = dateTo.Date;
        if (dFrom > dTo)
            (dFrom, dTo) = (dTo, dFrom);

        var lim = Math.Clamp(limit, 1, 5000);
        var q = (search ?? "").Trim();
        var fromStr = dFrom.ToString("yyyy-MM-dd");
        var toStr = dTo.ToString("yyyy-MM-dd") + " 23:59:59";
        var fromDay = dFrom.ToString("yyyy-MM-dd");
        var toDay = dTo.ToString("yyyy-MM-dd");

        using var conn = DatabaseService.OpenConnection();
        var rows = new List<StockIoRow>();

        LoadFromMovements(conn, rows, fromStr, toStr, q);
        LoadHistoricalSales(conn, rows, fromDay, toDay, q);
        LoadHistoricalPurchases(conn, rows, fromDay, toDay, q);

        // Recalcula Es. Anterior / Final quando faltar (legado)
        FillMissingStockLevels(conn, rows);

        IEnumerable<StockIoRow> filtered = rows;
        if (direction == StockIoDirectionFilter.Entradas)
            filtered = filtered.Where(r => r.IsEntry);
        else if (direction == StockIoDirectionFilter.Saidas)
            filtered = filtered.Where(r => !r.IsEntry);

        var ordered = filtered
            .OrderByDescending(r => r.CreatedAtRaw)
            .ThenByDescending(r => r.SortKey)
            .Take(lim)
            .ToList();

        double totE = 0, totS = 0;
        foreach (var r in ordered)
        {
            if (IsTransferOnly(r.Operation))
                continue;
            if (r.IsEntry) totE += r.Quantity;
            else totS += r.Quantity;
        }

        return new StockIoReportResult
        {
            DateFrom = dFrom,
            DateTo = dTo,
            Rows = ordered,
            Registros = ordered.Count,
            TotalEntradas = Math.Round(totE, 4),
            TotalSaidas = Math.Round(totS, 4),
        };
    }

    private static bool IsTransferOnly(string operation) =>
        operation.Contains("transferencia", StringComparison.OrdinalIgnoreCase)
        || operation.Contains("Transferência", StringComparison.OrdinalIgnoreCase);

    private static void LoadFromMovements(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        List<StockIoRow> rows,
        string fromStr, string toStr, string search)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT m.id, m.product_id,
                   IFNULL(COALESCE(NULLIF(p.name,''), 'Produto #' || m.product_id), ''),
                   IFNULL(p.code,''),
                   IFNULL(NULLIF(m.unit,''), IFNULL(p.unit,'UN')),
                   IFNULL(m.operation,''),
                   m.movement_type,
                   m.stock_before, m.quantity, m.stock_after,
                   IFNULL(m.user_name,''),
                   IFNULL(m.created_at,''),
                   IFNULL(m.notes,''),
                   IFNULL(m.ref_type,''), IFNULL(m.ref_id,0),
                   IFNULL(p.barcode,'')
            FROM movements m
            LEFT JOIN products p ON p.id = m.product_id
            WHERE m.created_at >= $from AND m.created_at <= $to
            ORDER BY m.created_at DESC, m.id DESC;
            """;
        cmd.Parameters.AddWithValue("$from", fromStr);
        cmd.Parameters.AddWithValue("$to", toStr);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(2);
            var code = reader.GetString(3);
            var barcode = reader.IsDBNull(15) ? "" : reader.GetString(15);
            if (!MatchesSearch(search, name, code, barcode))
                continue;

            var movType = reader.GetString(6);
            var isEntry = movType.Equals("entrada", StringComparison.OrdinalIgnoreCase);
            var operation = reader.GetString(5);
            var notes = reader.GetString(12);
            if (string.IsNullOrWhiteSpace(operation))
                operation = InferOperationFromNotes(notes, isEntry);
            else
                operation = RefineOperation(operation, notes, isEntry);

            // Observação antiga genérica → texto mais claro na tela
            if (notes.Equals("Estorno fiado / venda", StringComparison.OrdinalIgnoreCase))
                notes = "Estorno de fiado — estoque devolvido ao depósito";

            double? before = reader.IsDBNull(7) ? null : reader.GetDouble(7);
            double? after = reader.IsDBNull(9) ? null : reader.GetDouble(9);

            rows.Add(new StockIoRow
            {
                SortKey = reader.GetInt64(0),
                ProductId = reader.GetInt32(1),
                ProductName = name,
                ProductCode = code,
                Unit = reader.GetString(4),
                Operation = FormatOperation(operation, isEntry),
                StockBefore = before,
                Quantity = reader.GetDouble(8),
                IsEntry = isEntry,
                StockAfter = after,
                UserName = reader.GetString(10),
                CreatedAtRaw = reader.GetString(11),
                Notes = notes,
            });
        }
    }

    private static void LoadHistoricalSales(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        List<StockIoRow> rows,
        string fromDay, string toDay, string search)
    {
        // Vendas ativas sem movimento ref_type=sale
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT si.id, si.product_id,
                       IFNULL(NULLIF(si.product_name,''), IFNULL(p.name,'')),
                       IFNULL(NULLIF(si.product_code,''), IFNULL(p.code,'')),
                       IFNULL(NULLIF(si.unit,''), IFNULL(p.unit,'UN')),
                       CASE WHEN IFNULL(si.stock_qty,0) > 0.0001 THEN si.stock_qty ELSE si.quantity END,
                       IFNULL(s.created_at,''),
                       s.id,
                       IFNULL(p.barcode,''),
                       IFNULL(s.seller_id, 0)
                FROM sale_items si
                JOIN sales s ON s.id = si.sale_id
                LEFT JOIN products p ON p.id = si.product_id
                WHERE IFNULL(s.cancelled,0) = 0
                  AND date(s.created_at) >= $from AND date(s.created_at) <= $to
                  AND NOT EXISTS (
                      SELECT 1 FROM movements m
                      WHERE m.ref_type = 'sale' AND m.ref_id = s.id
                  )
                ORDER BY s.created_at DESC, si.id DESC;
                """;
            cmd.Parameters.AddWithValue("$from", fromDay);
            cmd.Parameters.AddWithValue("$to", toDay);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(2);
                var code = reader.GetString(3);
                var barcode = reader.IsDBNull(8) ? "" : reader.GetString(8);
                if (!MatchesSearch(search, name, code, barcode))
                    continue;

                var saleId = reader.GetInt32(7);
                rows.Add(new StockIoRow
                {
                    SortKey = -reader.GetInt64(0),
                    ProductId = reader.GetInt32(1),
                    ProductName = name,
                    ProductCode = code,
                    Unit = reader.GetString(4),
                    Operation = "Venda",
                    StockBefore = null,
                    Quantity = reader.GetDouble(5),
                    IsEntry = false,
                    StockAfter = null,
                    UserName = "",
                    CreatedAtRaw = reader.GetString(6),
                    Notes = $"Venda Pedido #{saleId}",
                });
            }
        }

        // Cancelamentos históricos não são sintetizados aqui (venda + estorno = líquido zero
        // e a data do cancelamento pode diferir da venda). Novos cancelamentos já vão em movements.
    }

    private static void LoadHistoricalPurchases(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        List<StockIoRow> rows,
        string fromDay, string toDay, string search)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT pi.id, pi.product_id,
                   IFNULL(NULLIF(pi.product_name,''), IFNULL(p.name,'')),
                   IFNULL(p.code,''),
                   IFNULL(p.unit,'UN'),
                   pi.quantity,
                   IFNULL(NULLIF(pu.entry_date,''), IFNULL(pu.created_at,'')),
                   pu.id,
                   IFNULL(pu.number,''),
                   IFNULL(pu.nfe_key,''),
                   IFNULL(p.barcode,'')
            FROM purchase_items pi
            JOIN purchases pu ON pu.id = pi.purchase_id
            LEFT JOIN products p ON p.id = pi.product_id
            WHERE IFNULL(pu.status,'') != 'cancelada'
              AND date(IFNULL(NULLIF(pu.entry_date,''), pu.created_at)) >= $from
              AND date(IFNULL(NULLIF(pu.entry_date,''), pu.created_at)) <= $to
              AND NOT EXISTS (
                  SELECT 1 FROM movements m
                  WHERE m.ref_type = 'purchase' AND m.ref_id = pu.id
              )
            ORDER BY pu.entry_date DESC, pi.id DESC;
            """;
        cmd.Parameters.AddWithValue("$from", fromDay);
        cmd.Parameters.AddWithValue("$to", toDay);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(2);
            var code = reader.GetString(3);
            var barcode = reader.IsDBNull(10) ? "" : reader.GetString(10);
            if (!MatchesSearch(search, name, code, barcode))
                continue;

            var purchaseId = reader.GetInt32(7);
            var number = reader.GetString(8);
            var nfeKey = reader.GetString(9);
            var isNfe = !string.IsNullOrWhiteSpace(nfeKey);
            var when = reader.GetString(6);
            if (when.Length == 10)
                when += " 12:00:00";

            rows.Add(new StockIoRow
            {
                SortKey = -reader.GetInt64(0),
                ProductId = reader.GetInt32(1),
                ProductName = name,
                ProductCode = code,
                Unit = reader.GetString(4),
                Operation = isNfe ? "Entrada NF-e" : "Entrada Compra",
                StockBefore = null,
                Quantity = reader.GetDouble(5),
                IsEntry = true,
                StockAfter = null,
                UserName = "",
                CreatedAtRaw = when,
                Notes = isNfe
                    ? $"XML NF {number}"
                    : $"Compra #{purchaseId}" + (string.IsNullOrWhiteSpace(number) ? "" : $" — NF {number}"),
            });
        }
    }

    /// <summary>
    /// Para linhas sem estoque anterior/final, estima caminhando do saldo atual
    /// para trás (por produto).
    /// </summary>
    private static void FillMissingStockLevels(
        Microsoft.Data.Sqlite.SqliteConnection conn, List<StockIoRow> rows)
    {
        if (rows.Count == 0) return;
        if (rows.All(r => r.StockBefore is not null && r.StockAfter is not null))
            return;

        var current = new Dictionary<int, double>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, IFNULL(stock,0) + IFNULL(stock_fridge,0) FROM products;
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                current[reader.GetInt32(0)] = reader.GetDouble(1);
        }

        // Do mais recente ao mais antigo: desfaz a movimentação
        foreach (var group in rows.GroupBy(r => r.ProductId))
        {
            var running = current.GetValueOrDefault(group.Key);
            foreach (var row in group.OrderByDescending(r => r.CreatedAtRaw).ThenByDescending(r => r.SortKey))
            {
                if (IsTransferOnly(row.Operation))
                {
                    row.StockBefore ??= running;
                    row.StockAfter ??= running;
                    continue;
                }

                var after = row.StockAfter ?? running;
                var before = row.StockBefore ?? (row.IsEntry ? after - row.Quantity : after + row.Quantity);
                row.StockBefore ??= Math.Round(before, 4);
                row.StockAfter ??= Math.Round(after, 4);
                running = row.StockBefore ?? before;
            }
        }
    }

    private static bool MatchesSearch(string search, string name, string code, string barcode)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;
        return name.Contains(search, StringComparison.OrdinalIgnoreCase)
               || code.Contains(search, StringComparison.OrdinalIgnoreCase)
               || barcode.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Corrige rótulos antigos gravados com operação genérica (ex.: cancelamento_venda
    /// usado também no estorno de fiado).
    /// </summary>
    private static string RefineOperation(string operation, string notes, bool isEntry)
    {
        var n = notes ?? "";
        var op = (operation ?? "").Trim().ToLowerInvariant();
        if (n.Contains("fiado", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Estorno fiado", StringComparison.OrdinalIgnoreCase))
            return "estorno_fiado";
        if (n.Contains("troca", StringComparison.OrdinalIgnoreCase)
            && (op is "cancelamento_venda" or "venda"))
            return isEntry ? "devolucao_troca" : "venda";
        return operation;
    }

    private static string InferOperationFromNotes(string notes, bool isEntry)
    {
        var n = notes ?? "";
        if (n.Contains("fiado", StringComparison.OrdinalIgnoreCase))
            return "estorno_fiado";
        if (n.Contains("NF-e", StringComparison.OrdinalIgnoreCase)
            || n.Contains("XML NF", StringComparison.OrdinalIgnoreCase))
            return "Entrada NF-e";
        if (n.Contains("geladeira", StringComparison.OrdinalIgnoreCase))
            return "Transferência Geladeira";
        if (n.Contains("Ajuste", StringComparison.OrdinalIgnoreCase)
            || n.Contains("saldo", StringComparison.OrdinalIgnoreCase))
            return "Ajuste Manual";
        if (n.Contains("Perda", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Avaria", StringComparison.OrdinalIgnoreCase))
            return "Perda/Avaria";
        if (n.Contains("troca", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Devolução", StringComparison.OrdinalIgnoreCase))
            return isEntry ? "devolucao_troca" : "venda";
        if (n.Contains("Cancelamento", StringComparison.OrdinalIgnoreCase))
            return "cancelamento_venda";
        if (n.Contains("Venda", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Pedido", StringComparison.OrdinalIgnoreCase))
            return isEntry ? "cancelamento_venda" : "venda";
        if (n.Contains("manual", StringComparison.OrdinalIgnoreCase))
            return isEntry ? "Entrada Manual" : "Saída Manual";
        return isEntry ? "Entrada" : "Saída";
    }

    private static string FormatOperation(string operation, bool isEntry)
    {
        var op = (operation ?? "").Trim().ToLowerInvariant();
        return op switch
        {
            "venda" => "Venda",
            "cancelamento_venda" => "Cancelamento de Venda",
            "estorno_fiado" => "Estorno Fiado",
            "devolucao_troca" => "Devolução / Troca",
            "entrada_nfe" => "Entrada NF-e",
            "entrada_compra" => "Entrada Compra",
            "estorno_compra" => "Estorno Compra",
            "ajuste_manual" or "entrada_manual" or "saida_manual" => "Ajuste Manual",
            "transferencia_geladeira" => "Transferência Geladeira",
            "perda" or "avaria" => "Perda/Avaria",
            "" => isEntry ? "Entrada" : "Saída",
            _ => CultureTitle(operation ?? (isEntry ? "Entrada" : "Saída")),
        };
    }

    private static string CultureTitle(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "—";
        s = s.Replace('_', ' ');
        return char.ToUpperInvariant(s[0]) + s[1..];
    }
}
