using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public static class OpenTabService
{
    /// <summary>Disparado quando lista/status de decks muda (ex.: pré-conta pelo celular).</summary>
    public static event Action? OpenTabsChanged;

    public static void RaiseOpenTabsChanged()
    {
        try { OpenTabsChanged?.Invoke(); }
        catch { /* UI listeners não podem derrubar o serviço */ }
    }

    public static IReadOnlyList<OpenTabListRow> ListOpen()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.name, t.customer_id, p.name AS customer_name, t.status, t.created_at,
                   IFNULL((SELECT COUNT(*) FROM open_tab_items i WHERE i.tab_id = t.id), 0) AS items_count,
                   IFNULL((SELECT SUM(i.subtotal) FROM open_tab_items i WHERE i.tab_id = t.id), 0) AS total,
                   t.notes, t.preconta_at
            FROM open_tabs t
            LEFT JOIN people p ON p.id = t.customer_id
            WHERE t.status = 'open'
            ORDER BY t.created_at DESC, t.id DESC;
            """;
        var list = new List<OpenTabListRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new OpenTabListRow
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                CustomerId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                CustomerName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Status = reader.GetString(4),
                CreatedAt = reader.GetString(5),
                ItemsCount = reader.GetInt32(6),
                Total = ProductPriceHelper.RoundPrice(reader.GetDouble(7)),
                Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
                PrecontaAt = reader.IsDBNull(9) ? null : reader.GetString(9),
            });
        }
        return list;
    }

    public static OpenTabDetail Get(int tabId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.name, t.customer_id, p.name AS customer_name, t.status,
                   t.sale_id, t.notes, t.created_at, t.settled_at, t.preconta_at
            FROM open_tabs t
            LEFT JOIN people p ON p.id = t.customer_id
            WHERE t.id = $id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", tabId);
        int id;
        string name;
        int? customerId;
        string? customerName;
        string status;
        int? saleId;
        string? notes;
        string createdAt;
        string? settledAt;
        string? precontaAt;

        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read())
                throw new OpenTabException("Deck não encontrado.");

            id = reader.GetInt32(0);
            name = reader.GetString(1);
            customerId = reader.IsDBNull(2) ? null : reader.GetInt32(2);
            customerName = reader.IsDBNull(3) ? null : reader.GetString(3);
            status = reader.GetString(4);
            saleId = reader.IsDBNull(5) ? null : reader.GetInt32(5);
            notes = reader.IsDBNull(6) ? null : reader.GetString(6);
            createdAt = reader.GetString(7);
            settledAt = reader.IsDBNull(8) ? null : reader.GetString(8);
            precontaAt = reader.IsDBNull(9) ? null : reader.GetString(9);
        }

        return new OpenTabDetail
        {
            Id = id,
            Name = name,
            CustomerId = customerId,
            CustomerName = customerName,
            Status = status,
            SaleId = saleId,
            Notes = notes,
            CreatedAt = createdAt,
            SettledAt = settledAt,
            PrecontaAt = precontaAt,
            Items = LoadItems(conn, tabId),
        };
    }

    public static int Create(string name, int? customerId = null, string? notes = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("abrir deck");
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new OpenTabException("Informe o nome do deck (ex.: Fernando).");
        if (name.Length > 80)
            name = name[..80];

        using var conn = DatabaseService.OpenConnection();
        if (customerId is > 0)
            EnsurePersonExists(conn, customerId.Value);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO open_tabs (name, customer_id, status, notes, created_at)
            VALUES ($name, $cust, 'open', $notes, $created);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$cust", (object?)customerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes.Trim());
        cmd.Parameters.AddWithValue("$created", DateBrHelper.NowUtcIso());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static OpenTabItemRow AddProduct(int tabId, int productId, double quantity = 1,
        double? unitPrice = null, double stockUnitsPerSale = 1)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("itens do deck");
        if (quantity <= 0)
            throw new OpenTabException("Quantidade inválida.");

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        RequireOpen(conn, tx, tabId);

        var product = LoadProduct(conn, tx, productId)
            ?? throw new OpenTabException("Produto não encontrado.");
        if (!product.Active)
            throw new OpenTabException($"Produto inativo: {product.Name}");

        var price = unitPrice is > 0 ? unitPrice.Value : product.SalePrice;
        if (price < 0)
            throw new OpenTabException("Preço inválido.");

        stockUnitsPerSale = stockUnitsPerSale < 0.0001 ? 1 : stockUnitsPerSale;

        // Junta linha igual (mesmo produto + mesmo modo de estoque)
        using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = """
                SELECT id, quantity, unit_price
                FROM open_tab_items
                WHERE tab_id = $tab AND product_id = $pid
                  AND ABS(IFNULL(stock_units_per_sale, 1) - $sus) < 0.0001
                LIMIT 1;
                """;
            find.Parameters.AddWithValue("$tab", tabId);
            find.Parameters.AddWithValue("$pid", productId);
            find.Parameters.AddWithValue("$sus", stockUnitsPerSale);
            using var reader = find.ExecuteReader();
            if (reader.Read())
            {
                var itemId = reader.GetInt32(0);
                var oldQty = reader.GetDouble(1);
                reader.Close();
                var newQty = ProductPriceHelper.RoundPrice(oldQty + quantity);
                var subtotal = ProductPriceHelper.RoundPrice(newQty * price);
                using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = """
                    UPDATE open_tab_items
                    SET quantity = $qty, unit_price = $price, subtotal = $sub
                    WHERE id = $id;
                    """;
                upd.Parameters.AddWithValue("$qty", newQty);
                upd.Parameters.AddWithValue("$price", price);
                upd.Parameters.AddWithValue("$sub", subtotal);
                upd.Parameters.AddWithValue("$id", itemId);
                upd.ExecuteNonQuery();
                tx.Commit();
                return GetItem(conn, itemId);
            }
        }

        var lineSub = ProductPriceHelper.RoundPrice(quantity * price);
        int newId;
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO open_tab_items (
                  tab_id, product_id, product_code, product_name, unit,
                  quantity, unit_price, subtotal, stock_units_per_sale, created_at
                ) VALUES (
                  $tab, $pid, $code, $name, $unit,
                  $qty, $price, $sub, $sus, $created
                );
                SELECT last_insert_rowid();
                """;
            ins.Parameters.AddWithValue("$tab", tabId);
            ins.Parameters.AddWithValue("$pid", product.Id);
            ins.Parameters.AddWithValue("$code", product.Code ?? "");
            ins.Parameters.AddWithValue("$name", product.Name);
            ins.Parameters.AddWithValue("$unit", product.Unit);
            ins.Parameters.AddWithValue("$qty", quantity);
            ins.Parameters.AddWithValue("$price", price);
            ins.Parameters.AddWithValue("$sub", lineSub);
            ins.Parameters.AddWithValue("$sus", stockUnitsPerSale);
            ins.Parameters.AddWithValue("$created", DateBrHelper.NowUtcIso());
            newId = Convert.ToInt32(ins.ExecuteScalar());
        }

        tx.Commit();
        return GetItem(conn, newId);
    }

    public static OpenTabItemRow AddFromScan(int tabId, string term, double quantity = 1)
    {
        var scan = PdvService.ResolveScan(term);
        if (scan is not null)
        {
            return AddProduct(tabId, scan.Product.Id, quantity * scan.Quantity,
                scan.UnitPrice, scan.StockUnitsPerSale);
        }

        var product = PdvService.FindProduct(term)
            ?? throw new OpenTabException("Produto não encontrado.");
        return AddProduct(tabId, product.Id, quantity);
    }

    public static void SetItemQuantity(int itemId, double quantity)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("itens do deck");
        if (quantity <= 0)
        {
            RemoveItem(itemId);
            return;
        }

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        var (tabId, unitPrice) = LoadItemTabAndPrice(conn, tx, itemId);
        RequireOpen(conn, tx, tabId);
        var subtotal = ProductPriceHelper.RoundPrice(quantity * unitPrice);
        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = "UPDATE open_tab_items SET quantity = $qty, subtotal = $sub WHERE id = $id;";
        upd.Parameters.AddWithValue("$qty", quantity);
        upd.Parameters.AddWithValue("$sub", subtotal);
        upd.Parameters.AddWithValue("$id", itemId);
        upd.ExecuteNonQuery();
        tx.Commit();
    }

    public static void RemoveItem(int itemId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("itens do deck");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        var (tabId, _) = LoadItemTabAndPrice(conn, tx, itemId);
        RequireOpen(conn, tx, tabId);
        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM open_tab_items WHERE id = $id;";
        del.Parameters.AddWithValue("$id", itemId);
        del.ExecuteNonQuery();
        tx.Commit();
    }

    public static void Cancel(int tabId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("cancelar deck");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        RequireOpen(conn, tx, tabId);
        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = """
            UPDATE open_tabs
            SET status = 'cancelled', settled_at = $at
            WHERE id = $id;
            """;
        upd.Parameters.AddWithValue("$at", DateBrHelper.NowUtcIso());
        upd.Parameters.AddWithValue("$id", tabId);
        upd.ExecuteNonQuery();
        tx.Commit();
    }

    public static IReadOnlyList<PdvCartLine> ToCartLines(int tabId)
    {
        var detail = Get(tabId);
        if (!detail.IsOpen)
            throw new OpenTabException("Este deck não está aberto.");
        if (detail.Items.Count == 0)
            throw new OpenTabException("Deck sem itens para cobrar.");

        var lines = new List<PdvCartLine>();
        var n = 0;
        foreach (var item in detail.Items)
        {
            lines.Add(new PdvCartLine
            {
                LineNum = ++n,
                ProductId = item.ProductId,
                Code = item.ProductCode,
                Name = item.ProductName,
                Unit = item.Unit,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                StockUnitsPerSale = item.StockUnitsPerSale < 0.0001 ? 1 : item.StockUnitsPerSale,
            });
        }
        return lines;
    }

    public static void MarkSettled(int tabId, int saleId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("fechar deck");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        MarkSettledCore(conn, tx, tabId, saleId);
        tx.Commit();
    }

    /// <summary>
    /// Marca o deck como settled na transação informada (RequireOpen + UPDATE).
    /// </summary>
    internal static void MarkSettledCore(
        SqliteConnection conn, SqliteTransaction tx, int tabId, int saleId)
    {
        RequireOpen(conn, tx, tabId);
        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = """
            UPDATE open_tabs
            SET status = 'settled', sale_id = $sale, settled_at = $at
            WHERE id = $id;
            """;
        upd.Parameters.AddWithValue("$sale", saleId);
        upd.Parameters.AddWithValue("$at", DateBrHelper.NowUtcIso());
        upd.Parameters.AddWithValue("$id", tabId);
        upd.ExecuteNonQuery();
    }

    public static void UpdateNotes(int tabId, string? notes)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("notas do deck");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        RequireOpen(conn, tx, tabId);
        var text = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (text is { Length: > 120 })
            text = text[..120];

        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = "UPDATE open_tabs SET notes = $notes WHERE id = $id;";
        upd.Parameters.AddWithValue("$notes", (object?)text ?? DBNull.Value);
        upd.Parameters.AddWithValue("$id", tabId);
        upd.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>
    /// Une itens de decks origem no deck destino e cancela as origens.
    /// </summary>
    public static void MergeTabs(int targetTabId, IReadOnlyList<int> sourceTabIds)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("unir decks");
        var sources = sourceTabIds
            .Where(id => id > 0 && id != targetTabId)
            .Distinct()
            .ToList();
        if (sources.Count == 0)
            throw new OpenTabException("Selecione pelo menos dois decks para juntar.");

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        RequireOpen(conn, tx, targetTabId);

        var noteParts = new List<string>();
        using (var ncmd = conn.CreateCommand())
        {
            ncmd.Transaction = tx;
            ncmd.CommandText = "SELECT IFNULL(notes,''), name FROM open_tabs WHERE id = $id LIMIT 1;";
            ncmd.Parameters.AddWithValue("$id", targetTabId);
            using var nr = ncmd.ExecuteReader();
            if (nr.Read())
            {
                var existing = nr.GetString(0).Trim();
                if (!string.IsNullOrWhiteSpace(existing))
                    noteParts.Add(existing);
            }
        }

        foreach (var sourceId in sources)
        {
            RequireOpen(conn, tx, sourceId);

            string sourceName;
            using (var sc = conn.CreateCommand())
            {
                sc.Transaction = tx;
                sc.CommandText = "SELECT name, IFNULL(notes,'') FROM open_tabs WHERE id = $id LIMIT 1;";
                sc.Parameters.AddWithValue("$id", sourceId);
                using var sr = sc.ExecuteReader();
                if (!sr.Read())
                    throw new OpenTabException("Deck de origem não encontrado.");
                sourceName = sr.GetString(0);
                var sn = sr.GetString(1).Trim();
                noteParts.Add(string.IsNullOrWhiteSpace(sn)
                    ? $"+ {sourceName}"
                    : $"+ {sourceName} ({sn})");
            }

            // Move itens; depois consolida linhas iguais no destino
            using (var mv = conn.CreateCommand())
            {
                mv.Transaction = tx;
                mv.CommandText = "UPDATE open_tab_items SET tab_id = $target WHERE tab_id = $source;";
                mv.Parameters.AddWithValue("$target", targetTabId);
                mv.Parameters.AddWithValue("$source", sourceId);
                mv.ExecuteNonQuery();
            }

            using (var cancel = conn.CreateCommand())
            {
                cancel.Transaction = tx;
                cancel.CommandText = """
                    UPDATE open_tabs
                    SET status = 'cancelled', settled_at = $at, notes = $merge
                    WHERE id = $id;
                    """;
                cancel.Parameters.AddWithValue("$at", DateBrHelper.NowUtcIso());
                cancel.Parameters.AddWithValue("$merge", $"unido em #{targetTabId}");
                cancel.Parameters.AddWithValue("$id", sourceId);
                cancel.ExecuteNonQuery();
            }
        }

        ConsolidateDuplicateItems(conn, tx, targetTabId);

        var mergedNotes = string.Join(" · ", noteParts);
        if (mergedNotes.Length > 120)
            mergedNotes = mergedNotes[..120];
        using (var notesCmd = conn.CreateCommand())
        {
            notesCmd.Transaction = tx;
            notesCmd.CommandText = "UPDATE open_tabs SET notes = $notes WHERE id = $id;";
            notesCmd.Parameters.AddWithValue("$notes",
                string.IsNullOrWhiteSpace(mergedNotes) ? DBNull.Value : mergedNotes);
            notesCmd.Parameters.AddWithValue("$id", targetTabId);
            notesCmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static void ConsolidateDuplicateItems(
        SqliteConnection conn, SqliteTransaction tx, int tabId)
    {
        // Agrupa por produto + fator de estoque + preço
        using var listCmd = conn.CreateCommand();
        listCmd.Transaction = tx;
        listCmd.CommandText = """
            SELECT id, product_id, IFNULL(stock_units_per_sale,1), unit_price, quantity
            FROM open_tab_items
            WHERE tab_id = $tab
            ORDER BY id ASC;
            """;
        listCmd.Parameters.AddWithValue("$tab", tabId);

        var rows = new List<(int Id, int Pid, double Sus, double Price, double Qty)>();
        using (var reader = listCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetDouble(2),
                    reader.GetDouble(3),
                    reader.GetDouble(4)));
            }
        }

        var keep = new Dictionary<string, (int Id, double Qty, double Price)>();
        var removeIds = new List<int>();
        foreach (var row in rows)
        {
            var key = $"{row.Pid}|{row.Sus:0.####}|{row.Price:0.####}";
            if (keep.TryGetValue(key, out var existing))
            {
                var newQty = ProductPriceHelper.RoundPrice(existing.Qty + row.Qty);
                keep[key] = (existing.Id, newQty, existing.Price);
                removeIds.Add(row.Id);
            }
            else
            {
                keep[key] = (row.Id, row.Qty, row.Price);
            }
        }

        foreach (var (_, value) in keep)
        {
            var sub = ProductPriceHelper.RoundPrice(value.Qty * value.Price);
            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE open_tab_items
                SET quantity = $qty, subtotal = $sub
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$qty", value.Qty);
            upd.Parameters.AddWithValue("$sub", sub);
            upd.Parameters.AddWithValue("$id", value.Id);
            upd.ExecuteNonQuery();
        }

        foreach (var id in removeIds)
        {
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM open_tab_items WHERE id = $id;";
            del.Parameters.AddWithValue("$id", id);
            del.ExecuteNonQuery();
        }
    }

    public static IReadOnlyList<string> BuildPreContaLines(int tabId)
    {
        var detail = Get(tabId);
        if (!detail.IsOpen)
            throw new OpenTabException("Este deck não está aberto.");

        var settings = AppSettingsService.GetPrinterSettings();
        var width = settings.PaperWidthMm is 58 or 80 ? settings.PaperWidthMm : 80;
        var cols = width <= 58 ? 32 : 42;

        static string Center(string text, int c)
        {
            text = (text ?? "").Trim();
            if (text.Length >= c) return text[..c];
            var pad = (c - text.Length) / 2;
            return new string(' ', pad) + text;
        }

        static string Pad(string left, string right, int c)
        {
            left = (left ?? "").Trim();
            right = (right ?? "").Trim();
            var space = c - left.Length - right.Length;
            if (space < 1)
                return left + " " + right;
            return left + new string(' ', space) + right;
        }

        var lines = new List<string>
        {
            Center("*** PRE-CONTA ***", cols),
            $"Deck: {detail.Name}",
        };
        if (!string.IsNullOrWhiteSpace(detail.CustomerName))
            lines.Add($"Cliente: {detail.CustomerName}");
        if (!string.IsNullOrWhiteSpace(detail.Notes))
            lines.Add($"Obs/Mesa: {detail.Notes.Trim()}");
        lines.Add($"Aberto: {DateBrHelper.FormatUtcToBrazil(detail.CreatedAt, "dd/MM/yyyy HH:mm")}");
        lines.Add($"Em: {DateTime.Now:dd/MM/yyyy HH:mm}");
        lines.Add(new string('-', cols));

        if (detail.Items.Count == 0)
        {
            lines.Add("(sem itens)");
        }
        else
        {
            foreach (var item in detail.Items)
            {
                lines.Add(item.ProductName);
                var left = $"  {item.Quantity:N3} x {ProductPriceHelper.MoneyBr(item.UnitPrice)}";
                var right = ProductPriceHelper.MoneyBr(item.Subtotal);
                lines.Add(Pad(left, right, cols));
            }
        }

        lines.Add(new string('-', cols));
        lines.Add(Pad("TOTAL", ProductPriceHelper.MoneyBr(detail.Total), cols));
        lines.Add("");
        lines.Add(Center("Conferencia — nao e cupom fiscal", cols));
        return lines;
    }

    public static void PrintPreConta(int tabId)
    {
        var body = BuildPreContaLines(tabId);
        PeripheralService.PrintReceiptLines(body);
        MarkPreConta(tabId, notifyCashier: false);
    }

    /// <summary>Garçom (celular) pede fechamento: mesa fica amarela e avisa o caixa.</summary>
    public static void RequestPreConta(int tabId)
    {
        var detail = Get(tabId);
        if (!detail.IsOpen)
            throw new OpenTabException("Este deck não está aberto.");
        if (detail.Items.Count == 0)
            throw new OpenTabException("Deck sem itens — não dá para solicitar pré-conta.");
        MarkPreConta(tabId, notifyCashier: true);
    }

    public static void MarkPreConta(int tabId, bool notifyCashier = false)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("pré-conta");
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE open_tabs
            SET preconta_at = datetime('now','localtime'),
                preconta_notify_pending = $notify
            WHERE id = $id AND status = 'open';
            """;
        cmd.Parameters.AddWithValue("$notify", notifyCashier ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", tabId);
        cmd.ExecuteNonQuery();
        RaiseOpenTabsChanged();
    }

    public static string FormatCashierPreContaLabel(OpenTabListRow tab)
    {
        var mesa = DeckTableHelper.TryParseTableNumber(tab.Name, tab.Notes);
        if (mesa is int n)
            return $"Mesa {n:00}";
        return string.IsNullOrWhiteSpace(tab.Name) ? $"Deck #{tab.Id}" : tab.Name.Trim();
    }

    public static IReadOnlyList<OpenTabListRow> ListPendingPreContaAlerts()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.name, t.customer_id, p.name AS customer_name, t.status, t.created_at,
                   IFNULL((SELECT COUNT(*) FROM open_tab_items i WHERE i.tab_id = t.id), 0) AS items_count,
                   IFNULL((SELECT SUM(i.subtotal) FROM open_tab_items i WHERE i.tab_id = t.id), 0) AS total,
                   t.notes, t.preconta_at
            FROM open_tabs t
            LEFT JOIN people p ON p.id = t.customer_id
            WHERE t.status = 'open' AND IFNULL(t.preconta_notify_pending, 0) = 1
            ORDER BY t.preconta_at ASC, t.id ASC;
            """;
        var list = new List<OpenTabListRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new OpenTabListRow
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                CustomerId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                CustomerName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Status = reader.GetString(4),
                CreatedAt = reader.GetString(5),
                ItemsCount = reader.GetInt32(6),
                Total = ProductPriceHelper.RoundPrice(reader.GetDouble(7)),
                Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
                PrecontaAt = reader.IsDBNull(9) ? null : reader.GetString(9),
            });
        }
        return list;
    }

    public static void AckPreContaAlerts(IEnumerable<int> tabIds)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("pré-conta");
        var ids = tabIds.Distinct().Where(id => id > 0).ToList();
        if (ids.Count == 0)
            return;
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        foreach (var id in ids)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE open_tabs
                SET preconta_notify_pending = 0
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static IReadOnlyList<OpenTabItemRow> LoadItems(SqliteConnection conn, int tabId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, tab_id, product_id, product_code, product_name, unit,
                   quantity, unit_price, subtotal, IFNULL(stock_units_per_sale, 1), created_at
            FROM open_tab_items
            WHERE tab_id = $tab
            ORDER BY id ASC;
            """;
        cmd.Parameters.AddWithValue("$tab", tabId);
        var list = new List<OpenTabItemRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new OpenTabItemRow
            {
                Id = reader.GetInt32(0),
                TabId = reader.GetInt32(1),
                ProductId = reader.GetInt32(2),
                ProductCode = reader.GetString(3),
                ProductName = reader.GetString(4),
                Unit = reader.GetString(5),
                Quantity = reader.GetDouble(6),
                UnitPrice = reader.GetDouble(7),
                Subtotal = reader.GetDouble(8),
                StockUnitsPerSale = reader.GetDouble(9),
                CreatedAt = reader.GetString(10),
            });
        }
        return list;
    }

    private static OpenTabItemRow GetItem(SqliteConnection conn, int itemId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, tab_id, product_id, product_code, product_name, unit,
                   quantity, unit_price, subtotal, IFNULL(stock_units_per_sale, 1), created_at
            FROM open_tab_items
            WHERE id = $id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", itemId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new OpenTabException("Item não encontrado.");
        return new OpenTabItemRow
        {
            Id = reader.GetInt32(0),
            TabId = reader.GetInt32(1),
            ProductId = reader.GetInt32(2),
            ProductCode = reader.GetString(3),
            ProductName = reader.GetString(4),
            Unit = reader.GetString(5),
            Quantity = reader.GetDouble(6),
            UnitPrice = reader.GetDouble(7),
            Subtotal = reader.GetDouble(8),
            StockUnitsPerSale = reader.GetDouble(9),
            CreatedAt = reader.GetString(10),
        };
    }

    private static void RequireOpen(SqliteConnection conn, SqliteTransaction tx, int tabId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT status FROM open_tabs WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", tabId);
        var status = cmd.ExecuteScalar() as string;
        if (status is null)
            throw new OpenTabException("Deck não encontrado.");
        if (!string.Equals(status, "open", StringComparison.OrdinalIgnoreCase))
            throw new OpenTabException("Este deck já foi fechado ou cancelado.");
    }

    private static (int TabId, double UnitPrice) LoadItemTabAndPrice(
        SqliteConnection conn, SqliteTransaction tx, int itemId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT tab_id, unit_price FROM open_tab_items WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", itemId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new OpenTabException("Item não encontrado.");
        return (reader.GetInt32(0), reader.GetDouble(1));
    }

    private static void EnsurePersonExists(SqliteConnection conn, int personId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM people WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", personId);
        if (cmd.ExecuteScalar() is null)
            throw new OpenTabException("Cliente não encontrado.");
    }

    private static Product? LoadProduct(SqliteConnection conn, SqliteTransaction tx, int productId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, IFNULL(code,''), name, IFNULL(unit,'UN'), IFNULL(sale_price,0),
                   IFNULL(stock, 0), IFNULL(active,1)
            FROM products
            WHERE id = $id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", productId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        return new Product
        {
            Id = reader.GetInt32(0),
            Code = reader.GetString(1),
            Name = reader.GetString(2),
            Unit = reader.GetString(3),
            SalePrice = reader.GetDouble(4),
            Stock = reader.GetDouble(5),
            Active = reader.GetInt32(6) != 0,
        };
    }
}
