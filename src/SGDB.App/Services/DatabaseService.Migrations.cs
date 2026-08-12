using Microsoft.Data.Sqlite;

namespace SGDB.Services;

public static partial class DatabaseService
{
    private static void ApplyPendingMigrations(SqliteConnection conn)
    {
        MigrateProductsTable(conn);
        MigratePeopleTable(conn);
        MigratePurchasesTable(conn);
        MigratePayablesTable(conn);
        MigrateCashTable(conn);
        MigrateSalesTable(conn);
        EnsureSaleExchangeTables(conn);
        MigrateFiadoTable(conn);
        MigrateLegacyCustomerIdsToPeople(conn);
        LegacySupplierMigration.Run(conn);
        MigrateUsersTable(conn);
        EnsurePaymentMethodFeesTable(conn);
        EnsureCatalogDescriptionColumns(conn);
        EnsureInventoryTables(conn);
        EnsureSellersTable(conn);
        EnsureContainerTypesTable(conn);
        EnsureExpenseCategoriesTable(conn);
        EnsurePriceTablesTable(conn);
        EnsureMovementsTable(conn);
        EnsureProductLotsTable(conn);
        EnsureBankTables(conn);
        EnsureVasilhameTables(conn);
        EnsureAuditLogTable(conn);
        EnsureOpenTabsTables(conn);
        EnsureDepositAwaitsTable(conn);
    }

    private static void EnsureCatalogDescriptionColumns(SqliteConnection conn)
    {
        foreach (var table in new[] { "product_units", "product_groups", "product_brands" })
        {
            if (!TableExists(conn, table))
                continue;
            var columns = GetTableColumns(conn, table);
            AddColumnIfMissing(conn, table, ref columns, "description", "TEXT NOT NULL DEFAULT ''");
        }
    }

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n LIMIT 1;";
        cmd.Parameters.AddWithValue("$n", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static void MigrateProductsTable(SqliteConnection conn)
    {
        var columns = GetTableColumns(conn, "products");
        if (columns.Count == 0)
            return;

        if (columns.Contains("stock_qty") && !columns.Contains("stock"))
            ExecuteSql(conn, "ALTER TABLE products RENAME COLUMN stock_qty TO stock;");

        columns = GetTableColumns(conn, "products");

        AddColumnIfMissing(conn, ref columns, "barcode", "TEXT");
        AddColumnIfMissing(conn, ref columns, "group_name", "TEXT");
        AddColumnIfMissing(conn, ref columns, "cost_price", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, ref columns, "min_stock", "INTEGER NOT NULL DEFAULT 5");
        AddColumnIfMissing(conn, ref columns, "location", "TEXT");
        AddColumnIfMissing(conn, ref columns, "extra_json", "TEXT NOT NULL DEFAULT '{}'");
        AddColumnIfMissing(conn, ref columns, "stock", "REAL NOT NULL DEFAULT 0");
        // Geladeira opcional: stock = depósito; stock_fridge = geladeira.
        AddColumnIfMissing(conn, ref columns, "stock_fridge", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, ref columns, "stock_fridge_min", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, ref columns, "created_at", "TEXT DEFAULT (datetime('now','localtime'))");

        columns = GetTableColumns(conn, "products");
        if (columns.Contains("created_at"))
        {
            ExecuteSql(conn, """
                UPDATE products
                SET created_at = datetime('now','localtime')
                WHERE created_at IS NULL OR TRIM(CAST(created_at AS TEXT)) = '';
                """);
        }
    }

    private static void MigratePeopleTable(SqliteConnection conn)
    {
        EnsurePeopleTable(conn);
        var columns = GetTableColumns(conn, "people");
        if (columns.Count == 0)
            return;

        // Compatível com banco da loja (app web): person_type NOT NULL
        AddColumnIfMissing(conn, "people", ref columns, "person_type", "TEXT NOT NULL DEFAULT 'cliente'");
        AddColumnIfMissing(conn, "people", ref columns, "person_kind", "TEXT NOT NULL DEFAULT 'juridica'");
        AddColumnIfMissing(conn, "people", ref columns, "created_at", "TEXT DEFAULT (datetime('now','localtime'))");
        AddColumnIfMissing(conn, "people", ref columns, "fiado_unit_surcharge", "REAL NOT NULL DEFAULT 0");

        columns = GetTableColumns(conn, "people");
        if (columns.Contains("person_type"))
        {
            ExecuteSql(conn, """
                UPDATE people
                SET person_type = 'cliente'
                WHERE person_type IS NULL OR TRIM(person_type) = '';
                """);
        }

        if (columns.Contains("created_at"))
        {
            ExecuteSql(conn, """
                UPDATE people
                SET created_at = datetime('now','localtime')
                WHERE created_at IS NULL OR TRIM(CAST(created_at AS TEXT)) = '';
                """);
        }
    }

    private static void MigratePurchasesTable(SqliteConnection conn)
    {
        EnsurePurchasesTable(conn);
        var columns = GetTableColumns(conn, "purchases");
        if (columns.Count == 0)
            return;

        // Banco da loja (app web) pode não ter colunas do nativo
        AddColumnIfMissing(conn, "purchases", ref columns, "entry_date", "TEXT");
        AddColumnIfMissing(conn, "purchases", ref columns, "series", "TEXT NOT NULL DEFAULT '1'");
        AddColumnIfMissing(conn, "purchases", ref columns, "nfe_key", "TEXT");
        AddColumnIfMissing(conn, "purchases", ref columns, "status", "TEXT NOT NULL DEFAULT 'aberta'");
        AddColumnIfMissing(conn, "purchases", ref columns, "total", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "purchases", ref columns, "gerar_estoque", "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfMissing(conn, "purchases", ref columns, "notes", "TEXT");
        AddColumnIfMissing(conn, "purchases", ref columns, "created_at", "TEXT DEFAULT (datetime('now','localtime'))");

        // Banco da loja: created_at NOT NULL sem default — preenche vazios
        columns = GetTableColumns(conn, "purchases");
        if (columns.Contains("created_at"))
        {
            ExecuteSql(conn, """
                UPDATE purchases
                SET created_at = datetime('now','localtime')
                WHERE created_at IS NULL OR TRIM(created_at) = '';
                """);
        }

        // entry_date vazio: copia emission_date quando existir
        if (columns.Contains("entry_date") && columns.Contains("emission_date"))
        {
            ExecuteSql(conn, """
                UPDATE purchases
                SET entry_date = emission_date
                WHERE entry_date IS NULL OR TRIM(entry_date) = '';
                """);
        }
    }

    private static void MigratePayablesTable(SqliteConnection conn)
    {
        EnsurePayablesTable(conn);
        var cols = GetTableColumns(conn, "payable_installments");
        AddColumnIfMissing(conn, "payable_installments", ref cols, "multa", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "payable_installments", ref cols, "notes", "TEXT");
        AddColumnIfMissing(conn, "payable_installments", ref cols, "financial_account", "TEXT");
    }

    private static void MigrateCashTable(SqliteConnection conn)
    {
        EnsureCashTable(conn);
        var sessionColumns = GetTableColumns(conn, "cash_sessions");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "closed_at", "TEXT");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "counted_amount", "REAL");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "expected_amount", "REAL");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "difference_amount", "REAL");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "notes", "TEXT");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "created_at",
            "TEXT NOT NULL DEFAULT (datetime('now','localtime'))");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "float_notes_amount", "REAL");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "float_coins_amount", "REAL");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "deposit_amount", "REAL");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "deposit_status", "TEXT");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "deposit_confirmed_amount", "REAL");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "deposit_confirmed_at", "TEXT");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "deposit_notes", "TEXT");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "opened_by_user_id", "INTEGER");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "opened_by_user_name", "TEXT");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "closed_by_user_id", "INTEGER");
        AddColumnIfMissing(conn, "cash_sessions", ref sessionColumns, "closed_by_user_name", "TEXT");
        ExecuteSql(conn, """
            UPDATE cash_sessions
            SET created_at = datetime('now','localtime')
            WHERE created_at IS NULL OR TRIM(created_at) = '';
            """);

        var movementColumns = GetTableColumns(conn, "cash_movements");
        AddColumnIfMissing(conn, "cash_movements", ref movementColumns, "party_name", "TEXT");
        AddColumnIfMissing(conn, "cash_movements", ref movementColumns, "payment_type", "TEXT");
        AddColumnIfMissing(conn, "cash_movements", ref movementColumns, "affects_balance", "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfMissing(conn, "cash_movements", ref movementColumns, "ref_type", "TEXT");
        AddColumnIfMissing(conn, "cash_movements", ref movementColumns, "ref_id", "INTEGER");
        AddColumnIfMissing(conn, "cash_movements", ref movementColumns, "notes", "TEXT");
        AddColumnIfMissing(conn, "cash_movements", ref movementColumns, "created_at",
            "TEXT NOT NULL DEFAULT (datetime('now','localtime'))");
        ExecuteSql(conn, """
            UPDATE cash_movements
            SET created_at = datetime('now','localtime')
            WHERE created_at IS NULL OR TRIM(created_at) = '';
            """);
    }

    private static void MigrateSalesTable(SqliteConnection conn)
    {
        EnsureSalesTable(conn);
        var columns = GetTableColumns(conn, "sales");
        AddColumnIfMissing(conn, "sales", ref columns, "seller_id", "INTEGER");
        AddColumnIfMissing(conn, "sales", ref columns, "notes", "TEXT");
        AddColumnIfMissing(conn, "sales", ref columns, "cancelled", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "sales", ref columns, "cash_received", "REAL");
        AddColumnIfMissing(conn, "sales", ref columns, "change_amount", "REAL");

        var itemColumns = GetTableColumns(conn, "sale_items");
        AddColumnIfMissing(conn, "sale_items", ref itemColumns, "product_code", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(conn, "sale_items", ref itemColumns, "product_name", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(conn, "sale_items", ref itemColumns, "unit", "TEXT NOT NULL DEFAULT 'UN'");
        AddColumnIfMissing(conn, "sale_items", ref itemColumns, "quantity", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "sale_items", ref itemColumns, "unit_price", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "sale_items", ref itemColumns, "subtotal", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "sale_items", ref itemColumns, "stock_qty", "REAL NOT NULL DEFAULT 0");
    }

    private static void MigrateFiadoTable(SqliteConnection conn)
    {
        EnsureFiadoTable(conn);
    }

    /// <summary>
    /// Bancos antigos: sales/fiado_payments.customer_id → customers.id.
    /// O app usa people.id. Remapeia via customers.person_id e recria o FK.
    /// </summary>
    private static void MigrateLegacyCustomerIdsToPeople(SqliteConnection conn)
    {
        if (!TableExists(conn, "customers"))
            return;

        var salesParent = GetForeignKeyParent(conn, "sales", "customer_id");
        var fiadoParent = GetForeignKeyParent(conn, "fiado_payments", "customer_id");
        if (salesParent != "customers" && fiadoParent != "customers")
            return;

        ExecuteSql(conn, "PRAGMA foreign_keys=OFF;");
        using var tx = conn.BeginTransaction();
        try
        {
            if (salesParent == "customers")
                RebuildSalesCustomerFkToPeople(conn, tx);
            if (fiadoParent == "customers")
                RebuildFiadoPaymentsCustomerFkToPeople(conn, tx);
            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { /* ignore */ }
            throw;
        }
        finally
        {
            ExecuteSql(conn, "PRAGMA foreign_keys=ON;");
        }
    }

    private static void RebuildSalesCustomerFkToPeople(SqliteConnection conn, SqliteTransaction tx)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS sales__people_fk (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_date TEXT NOT NULL,
                    total REAL NOT NULL DEFAULT 0,
                    payment_type TEXT NOT NULL DEFAULT 'Dinheiro',
                    customer_id INTEGER,
                    seller_id INTEGER,
                    notes TEXT,
                    cancelled INTEGER NOT NULL DEFAULT 0,
                    cash_received REAL,
                    change_amount REAL,
                    created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                    FOREIGN KEY (customer_id) REFERENCES people(id)
                );
                """;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO sales__people_fk (
                    id, session_date, total, payment_type, customer_id, seller_id, notes,
                    cancelled, cash_received, change_amount, created_at
                )
                SELECT
                    s.id,
                    s.session_date,
                    s.total,
                    s.payment_type,
                    CASE
                        WHEN s.customer_id IS NULL THEN NULL
                        ELSE COALESCE(
                            (SELECT c.person_id FROM customers c WHERE c.id = s.customer_id),
                            CASE WHEN EXISTS(SELECT 1 FROM people p WHERE p.id = s.customer_id)
                                 THEN s.customer_id ELSE NULL END
                        )
                    END,
                    s.seller_id,
                    s.notes,
                    IFNULL(s.cancelled, 0),
                    s.cash_received,
                    s.change_amount,
                    s.created_at
                FROM sales s;
                """;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DROP TABLE sales;";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "ALTER TABLE sales__people_fk RENAME TO sales;";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_sales_session_date ON sales(session_date);
                CREATE INDEX IF NOT EXISTS idx_sale_items_sale ON sale_items(sale_id);
                """;
            cmd.ExecuteNonQuery();
        }
    }

    private static void RebuildFiadoPaymentsCustomerFkToPeople(SqliteConnection conn, SqliteTransaction tx)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS fiado_payments__people_fk (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    customer_id INTEGER NOT NULL,
                    amount REAL NOT NULL DEFAULT 0,
                    interest_amount REAL NOT NULL DEFAULT 0,
                    payment_type TEXT NOT NULL DEFAULT 'Dinheiro',
                    payment_date TEXT NOT NULL,
                    notes TEXT,
                    reversed INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                    FOREIGN KEY (customer_id) REFERENCES people(id)
                );
                """;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO fiado_payments__people_fk (
                    id, customer_id, amount, interest_amount, payment_type,
                    payment_date, notes, reversed, created_at
                )
                SELECT
                    f.id,
                    COALESCE(
                        (SELECT c.person_id FROM customers c WHERE c.id = f.customer_id),
                        CASE WHEN EXISTS(SELECT 1 FROM people p WHERE p.id = f.customer_id)
                             THEN f.customer_id ELSE f.customer_id END
                    ),
                    f.amount,
                    IFNULL(f.interest_amount, 0),
                    f.payment_type,
                    f.payment_date,
                    f.notes,
                    IFNULL(f.reversed, 0),
                    f.created_at
                FROM fiado_payments f;
                """;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DROP TABLE fiado_payments;";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "ALTER TABLE fiado_payments__people_fk RENAME TO fiado_payments;";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_fiado_payments_customer ON fiado_payments(customer_id);
                CREATE INDEX IF NOT EXISTS idx_fiado_payments_date ON fiado_payments(payment_date);
                """;
            cmd.ExecuteNonQuery();
        }
        }

    private static string? GetForeignKeyParent(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA foreign_key_list({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var from = reader["from"]?.ToString();
            if (string.Equals(from, column, StringComparison.OrdinalIgnoreCase))
                return reader["table"]?.ToString();
        }
        return null;
    }

    private static void MigrateUsersTable(SqliteConnection conn)
    {
        var columns = GetTableColumns(conn, "users");
        if (columns.Count == 0)
            return;

        AddColumnIfMissing(conn, "users", ref columns, "permissions_json", "TEXT");
        AddColumnIfMissing(conn, "users", ref columns, "email", "TEXT");
    }

    private static void AddColumnIfMissing(SqliteConnection conn, ref HashSet<string> columns, string name, string ddl)
    {
        if (columns.Contains(name))
            return;
        ExecuteSql(conn, $"ALTER TABLE products ADD COLUMN {name} {ddl};");
        columns.Add(name);
    }

    private static void AddColumnIfMissing(
        SqliteConnection conn,
        string table,
        ref HashSet<string> columns,
        string name,
        string ddl)
    {
        if (columns.Contains(name))
            return;
        ExecuteSql(conn, $"ALTER TABLE {table} ADD COLUMN {name} {ddl};");
        columns.Add(name);
    }

    private static HashSet<string> GetTableColumns(SqliteConnection conn, string table)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            set.Add(reader.GetString(1));
        return set;
    }

    private static void ExecuteSql(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
