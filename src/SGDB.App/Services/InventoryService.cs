using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Inventário físico do DEPÓSITO: abre uma sessão de contagem (todos os produtos ativos ou de um
/// grupo), registra as quantidades contadas no depósito e, ao consolidar, ajusta
/// <c>products.stock</c> via <see cref="StockService.Adjust"/> (modo Saldo).
/// <c>stock_fridge</c> não é alterado.
/// </summary>
public static class InventoryService
{
    /// <summary>
    /// Somente testes: invocado imediatamente antes de marcar a sessão como consolidada.
    /// Deve permanecer null em produção.
    /// </summary>
    public static Action? TestBeforeMarkSessionConsolidated { get; set; }

    public static InventorySession CreateSession(string? groupName = null, string? notes = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("inventário");
        if (GetOpenSession() is not null)
            throw new InvalidOperationException("Já existe um inventário em aberto. Consolide ou cancele antes de iniciar outro.");

        var group = string.IsNullOrWhiteSpace(groupName) ? null : groupName.Trim().ToUpperInvariant();

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        int sessionId;
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO inventory_sessions (status, group_name, notes)
                VALUES ('aberta', $group, $notes);
                SELECT last_insert_rowid();
                """;
            ins.Parameters.AddWithValue("$group", (object?)group ?? DBNull.Value);
            ins.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
            sessionId = Convert.ToInt32(ins.ExecuteScalar());
        }

        using (var sel = conn.CreateCommand())
        {
            sel.Transaction = tx;
            var sql = "SELECT id, IFNULL(stock,0) FROM products WHERE IFNULL(active,1) = 1";
            if (group is not null)
                sql += " AND UPPER(IFNULL(group_name,'')) = $group";
            sql += " ORDER BY name;";
            sel.CommandText = sql;
            if (group is not null)
                sel.Parameters.AddWithValue("$group", group);

            var products = new List<(int Id, double Stock)>();
            using (var reader = sel.ExecuteReader())
            {
                while (reader.Read())
                    products.Add((reader.GetInt32(0), reader.GetDouble(1)));
            }

            foreach (var (productId, stock) in products)
            {
                using var insItem = conn.CreateCommand();
                insItem.Transaction = tx;
                insItem.CommandText = """
                    INSERT INTO inventory_items (session_id, product_id, theoretical_qty, counted_qty)
                    VALUES ($session, $product, $qty, NULL);
                    """;
                insItem.Parameters.AddWithValue("$session", sessionId);
                insItem.Parameters.AddWithValue("$product", productId);
                insItem.Parameters.AddWithValue("$qty", stock);
                insItem.ExecuteNonQuery();
            }
        }

        tx.Commit();
        return GetSessionById(sessionId) ?? throw new InvalidOperationException("Falha ao criar inventário.");
    }

    public static InventorySession? GetOpenSession()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, status, group_name, notes, created_at, closed_at
            FROM inventory_sessions WHERE status = 'aberta'
            ORDER BY id DESC LIMIT 1;
            """;
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadSession(reader) : null;
    }

    public static InventorySession? GetSessionById(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, status, group_name, notes, created_at, closed_at
            FROM inventory_sessions WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadSession(reader) : null;
    }

    public static IReadOnlyList<InventorySession> ListSessions(int limit = 100)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, status, group_name, notes, created_at, closed_at
            FROM inventory_sessions ORDER BY id DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        var list = new List<InventorySession>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadSession(reader));
        return list;
    }

    public static IReadOnlyList<InventoryItem> ListItems(int sessionId, bool onlyPending = false)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = """
            SELECT i.id, i.session_id, i.product_id, IFNULL(p.code,''), IFNULL(p.barcode,''), IFNULL(p.name,''),
                   IFNULL(p.unit,'UN'), i.theoretical_qty, i.counted_qty, i.notes,
                   i.counted_at, i.count_baseline_qty,
                   IFNULL(p.stock_fridge, 0), IFNULL(p.stock_fridge_min, 0), IFNULL(p.stock, 0)
            FROM inventory_items i
            LEFT JOIN products p ON p.id = i.product_id
            WHERE i.session_id = $session
            """;
        if (onlyPending)
            sql += " AND i.counted_qty IS NULL";
        sql += " ORDER BY p.name;";
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$session", sessionId);

        var list = new List<InventoryItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new InventoryItem
            {
                Id = reader.GetInt32(0),
                SessionId = reader.GetInt32(1),
                ProductId = reader.GetInt32(2),
                ProductCode = reader.GetString(3),
                ProductBarcode = reader.IsDBNull(4) ? "" : reader.GetString(4),
                ProductName = reader.GetString(5),
                Unit = reader.GetString(6),
                TheoreticalQty = reader.GetDouble(7),
                CountedQty = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
                CountedAt = reader.IsDBNull(10) ? null : reader.GetString(10),
                CountBaselineQty = reader.IsDBNull(11) ? null : reader.GetDouble(11),
                StockFridge = reader.GetDouble(12),
                StockFridgeMin = (int)Math.Round(reader.GetDouble(13)),
                CurrentStock = reader.GetDouble(14),
            });
        }
        return list;
    }

    public static void SetCounted(int itemId, double countedQty, string? notes = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("inventário");
        if (countedQty < 0)
            throw new InvalidOperationException("Quantidade contada não pode ser negativa.");

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        var sessionId = GetSessionIdForItem(conn, tx, itemId)
            ?? throw new InvalidOperationException("Item de inventário não encontrado.");
        RequireOpen(conn, tx, sessionId);

        int productId;
        using (var sel = conn.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT product_id FROM inventory_items WHERE id = $id LIMIT 1;";
            sel.Parameters.AddWithValue("$id", itemId);
            productId = Convert.ToInt32(sel.ExecuteScalar()
                ?? throw new InvalidOperationException("Item de inventário não encontrado."));
        }

        double currentStock;
        using (var stockCmd = conn.CreateCommand())
        {
            stockCmd.Transaction = tx;
            stockCmd.CommandText = "SELECT IFNULL(stock, 0) FROM products WHERE id = $id LIMIT 1;";
            stockCmd.Parameters.AddWithValue("$id", productId);
            var raw = stockCmd.ExecuteScalar()
                ?? throw new InvalidOperationException("Produto não encontrado.");
            currentStock = Convert.ToDouble(raw);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE inventory_items
                SET counted_qty = $qty,
                    notes = $notes,
                    count_baseline_qty = $baseline,
                    counted_at = datetime('now','localtime')
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$qty", Math.Round(countedQty, 4));
            cmd.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$baseline", Math.Round(currentStock, 4));
            cmd.Parameters.AddWithValue("$id", itemId);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public static IReadOnlyList<InventoryDivergenceRow> ListDivergences(int sessionId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT i.id, i.product_id, IFNULL(p.code,''), IFNULL(p.name,''), IFNULL(p.unit,'UN'),
                   i.theoretical_qty, i.counted_qty, IFNULL(p.cost_price,0)
            FROM inventory_items i
            LEFT JOIN products p ON p.id = i.product_id
            WHERE i.session_id = $session
              AND i.counted_qty IS NOT NULL
              AND ABS(i.counted_qty - i.theoretical_qty) > 0.0009
            ORDER BY p.name;
            """;
        cmd.Parameters.AddWithValue("$session", sessionId);

        var list = new List<InventoryDivergenceRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var theoretical = reader.GetDouble(5);
            var counted = reader.GetDouble(6);
            list.Add(new InventoryDivergenceRow
            {
                ItemId = reader.GetInt32(0),
                ProductId = reader.GetInt32(1),
                ProductCode = reader.GetString(2),
                ProductName = reader.GetString(3),
                Unit = reader.GetString(4),
                TheoreticalQty = theoretical,
                CountedQty = counted,
                Difference = Math.Round(counted - theoretical, 3),
                Cost = reader.GetDouble(7),
            });
        }
        return list;
    }

    public static InventoryConsolidateResult Consolidate(int sessionId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("inventário");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        RequireOpen(conn, tx, sessionId);

        var rows = new List<(int ProductId, double Counted)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT product_id, counted_qty FROM inventory_items
                WHERE session_id = $session AND counted_qty IS NOT NULL
                ORDER BY product_id;
                """;
            cmd.Parameters.AddWithValue("$session", sessionId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.GetInt32(0), reader.GetDouble(1)));
        }

        var conflicts = DetectConcurrencyConflicts(conn, tx, sessionId);
        if (conflicts.Count > 0)
            throw new InventoryConcurrencyException(conflicts);

        var adjusted = 0;
        double totalPositive = 0, totalNegative = 0;
        foreach (var (productId, counted) in rows)
        {
            var result = StockService.Adjust(
                conn,
                tx,
                productId,
                StockAdjustMode.Saldo,
                newStock: counted,
                notes: $"Inventário #{sessionId} — consolidação");
            if (result.Quantity <= 0)
                continue;
            adjusted++;
            if (result.MovementType == "entrada")
                totalPositive += result.Quantity;
            else
                totalNegative += result.Quantity;
        }

        TestBeforeMarkSessionConsolidated?.Invoke();

        using (var close = conn.CreateCommand())
        {
            close.Transaction = tx;
            close.CommandText = """
                UPDATE inventory_sessions
                SET status = 'consolidada', closed_at = datetime('now','localtime')
                WHERE id = $id;
                """;
            close.Parameters.AddWithValue("$id", sessionId);
            close.ExecuteNonQuery();
        }

        tx.Commit();

        return new InventoryConsolidateResult
        {
            SessionId = sessionId,
            AdjustedCount = adjusted,
            TotalPositiveQty = Math.Round(totalPositive, 3),
            TotalNegativeQty = Math.Round(totalNegative, 3),
        };
    }

    /// <summary>
    /// ETAPA 60D — conflito relativo à última contagem (count_baseline_qty / counted_at).
    /// Itens contados sem baseline/timestamp exigem recontagem (legado).
    /// Movements: created_at &gt; counted_at (não &gt;=).
    /// </summary>
    private static List<InventoryConcurrencyConflict> DetectConcurrencyConflicts(
        SqliteConnection conn,
        SqliteTransaction tx,
        int sessionId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT i.product_id,
                   IFNULL(p.code, ''),
                   IFNULL(p.name, ''),
                   i.theoretical_qty,
                   IFNULL(p.stock, 0),
                   i.count_baseline_qty,
                   i.counted_at,
                   CASE
                     WHEN i.counted_at IS NOT NULL AND EXISTS (
                       SELECT 1 FROM movements m
                       WHERE m.product_id = i.product_id
                         AND m.created_at > i.counted_at
                     ) THEN 1 ELSE 0
                   END
            FROM inventory_items i
            LEFT JOIN products p ON p.id = i.product_id
            WHERE i.session_id = $session
              AND i.counted_qty IS NOT NULL
            ORDER BY IFNULL(p.name, ''), i.product_id;
            """;
        cmd.Parameters.AddWithValue("$session", sessionId);

        var conflicts = new List<InventoryConcurrencyConflict>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var theoretical = reader.GetDouble(3);
            var current = reader.GetDouble(4);
            var baselineNull = reader.IsDBNull(5);
            var countedAtNull = reader.IsDBNull(6);
            var baseline = baselineNull ? (double?)null : reader.GetDouble(5);
            var countedAt = countedAtNull ? null : reader.GetString(6);
            var hasMovement = reader.GetInt32(7) != 0;
            var requiresRecount = baselineNull || countedAtNull;
            var stockDiverged = !requiresRecount
                && Math.Abs(current - baseline!.Value) > InventoryConcurrencyException.StockTolerance;

            if (!requiresRecount && !stockDiverged && !hasMovement)
                continue;

            conflicts.Add(new InventoryConcurrencyConflict
            {
                ProductId = reader.GetInt32(0),
                ProductCode = reader.GetString(1),
                ProductName = reader.GetString(2),
                TheoreticalQty = theoretical,
                CurrentStock = current,
                CountBaselineQty = baseline,
                CountedAt = countedAt,
                RequiresRecount = requiresRecount,
                StockDivergedFromBaseline = stockDiverged,
                HasMovementSinceCount = hasMovement,
            });
        }
        return conflicts;
    }

    public static void Cancel(int sessionId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("inventário");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        RequireOpen(conn, tx, sessionId);

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE inventory_sessions
            SET status = 'cancelada', closed_at = datetime('now','localtime')
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    private static void RequireOpen(SqliteConnection conn, SqliteTransaction tx, int sessionId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT status FROM inventory_sessions WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", sessionId);
        var status = cmd.ExecuteScalar() as string
            ?? throw new InvalidOperationException("Inventário não encontrado.");
        if (status != "aberta")
            throw new InvalidOperationException("Este inventário já foi encerrado.");
    }

    private static int? GetSessionIdForItem(SqliteConnection conn, SqliteTransaction tx, int itemId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT session_id FROM inventory_items WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", itemId);
        var result = cmd.ExecuteScalar();
        return result is null ? null : Convert.ToInt32(result);
    }

    private static InventorySession ReadSession(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Status = reader.GetString(1),
        GroupName = reader.IsDBNull(2) ? null : reader.GetString(2),
        Notes = reader.IsDBNull(3) ? null : reader.GetString(3),
        CreatedAt = reader.GetString(4),
        ClosedAt = reader.IsDBNull(5) ? null : reader.GetString(5),
    };
}
