using Microsoft.Data.Sqlite;

namespace SGDB.Services;

public static partial class DatabaseService
{
    private static void EnsureSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                login TEXT NOT NULL UNIQUE,
                nome TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                role TEXT NOT NULL DEFAULT 'admin',
                active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS products (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                code TEXT,
                name TEXT NOT NULL,
                unit TEXT NOT NULL DEFAULT 'UN',
                sale_price REAL NOT NULL DEFAULT 0,
                stock_qty REAL NOT NULL DEFAULT 0,
                active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS product_brands (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS product_groups (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS product_units (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        MigrateProductsTable(conn);
        EnsureProductIndexes(conn);
        EnsureCatalogDescriptionColumns(conn);
        EnsureInventoryTables(conn);
        EnsurePeopleTable(conn);
        EnsurePurchasesTable(conn);
        EnsurePurchaseItemLotsTable(conn);
        EnsurePayablesTable(conn);
        EnsureCashTable(conn);
        EnsureSalesTable(conn);
        EnsurePixIntentsTable(conn);
        EnsureFiadoTable(conn);
        EnsurePaymentMethodFeesTable(conn);
        EnsureOpenTabsTables(conn);
        EnsureDepositAwaitsTable(conn);
        SeedDefaultUnits(conn);
    }

    private static void EnsurePaymentMethodFeesTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS payment_method_fees (
                method_id TEXT PRIMARY KEY,
                fee_percent REAL NOT NULL DEFAULT 0,
                settlement_days INTEGER NOT NULL DEFAULT 0
            );
            """);
        var columns = GetTableColumns(conn, "payment_method_fees");
        var hadSettlement = columns.Contains("settlement_days");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "settlement_days", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "fee_fixed", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "bank_account_id", "INTEGER");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "name", "TEXT");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "api_label", "TEXT");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "movement_type", "TEXT");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "active", "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "pdv_key", "TEXT");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "sort_order", "INTEGER NOT NULL DEFAULT 100");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "notes", "TEXT");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "fee_editable", "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "is_system", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "destination_kind", "TEXT NOT NULL DEFAULT 'banco'");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "created_at", "TEXT");
        AddColumnIfMissing(conn, "payment_method_fees", ref columns, "updated_at", "TEXT");
        // Bancos legados (Gestão) exigem updated_at NOT NULL — preenche vazios.
        try
        {
            ExecuteSql(conn, """
                UPDATE payment_method_fees
                SET updated_at = datetime('now')
                WHERE updated_at IS NULL OR TRIM(CAST(updated_at AS TEXT)) = '';
                """);
        }
        catch { /* coluna pode não existir em DB novo sem a constraint */ }

        if (!hadSettlement)
        {
            // Backfill sensato só na 1ª migração (crédito D+30; débito D+1).
            ExecuteSql(conn, "UPDATE payment_method_fees SET settlement_days = 30 WHERE method_id = 'credito';");
            ExecuteSql(conn, "UPDATE payment_method_fees SET settlement_days = 1 WHERE method_id = 'debito';");
        }

        PaymentMethodsService.EnsureSeeded(conn);
    }

    private static void EnsureSellersTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS sellers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                code TEXT NOT NULL UNIQUE,
                name TEXT NOT NULL,
                phone TEXT,
                cpf TEXT,
                commission_percent REAL NOT NULL DEFAULT 0,
                notes TEXT,
                active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_sellers_name ON sellers(name);");
    }

    private static void EnsureContainerTypesTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS container_types (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                sale_price REAL NOT NULL DEFAULT 0,
                stock REAL NOT NULL DEFAULT 0,
                active INTEGER NOT NULL DEFAULT 1,
                notes TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """);
    }

    private static void EnsureExpenseCategoriesTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS expense_categories (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                hint TEXT,
                active INTEGER NOT NULL DEFAULT 1,
                sort_order INTEGER NOT NULL DEFAULT 100,
                is_system INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """);

        // Banco legado (SQLAlchemy) já pode ter a tabela sem defaults — garante colunas.
        var columns = GetTableColumns(conn, "expense_categories");
        AddColumnIfMissing(conn, "expense_categories", ref columns, "hint", "TEXT");
        AddColumnIfMissing(conn, "expense_categories", ref columns, "sort_order", "INTEGER NOT NULL DEFAULT 100");
        AddColumnIfMissing(conn, "expense_categories", ref columns, "is_system", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "expense_categories", ref columns, "active", "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfMissing(conn, "expense_categories", ref columns, "created_at", "TEXT NOT NULL DEFAULT (datetime('now'))");

        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_expense_categories_name ON expense_categories(name);");
        ExpenseCategoriesService.EnsureSeeded(conn);
    }

    private static void EnsureBankTables(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS bank_accounts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                bank_name TEXT,
                agency TEXT,
                account_number TEXT,
                account_type TEXT NOT NULL DEFAULT 'corrente',
                pix_key TEXT,
                opening_balance REAL NOT NULL DEFAULT 0,
                active INTEGER NOT NULL DEFAULT 1,
                notes TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
            );

            CREATE TABLE IF NOT EXISTS bank_movements (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_id INTEGER NOT NULL,
                movement_date TEXT NOT NULL,
                posted_date TEXT,
                kind TEXT NOT NULL,
                description TEXT NOT NULL DEFAULT '',
                party_name TEXT,
                payment_type TEXT,
                amount_in REAL NOT NULL DEFAULT 0,
                amount_out REAL NOT NULL DEFAULT 0,
                fee_amount REAL NOT NULL DEFAULT 0,
                reconciliation_status TEXT NOT NULL DEFAULT 'pendente',
                reconciled_at TEXT,
                ref_type TEXT,
                ref_id INTEGER,
                notes TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                FOREIGN KEY (account_id) REFERENCES bank_accounts(id)
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_bank_movements_account ON bank_movements(account_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_bank_movements_date ON bank_movements(movement_date);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_bank_movements_recon ON bank_movements(reconciliation_status);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_bank_movements_ref ON bank_movements(ref_type, ref_id);");

        var accCols = GetTableColumns(conn, "bank_accounts");
        AddColumnIfMissing(conn, "bank_accounts", ref accCols, "default_operator", "TEXT");

        var movCols = GetTableColumns(conn, "bank_movements");
        AddColumnIfMissing(conn, "bank_movements", ref movCols, "operator_name", "TEXT");
        AddColumnIfMissing(conn, "bank_movements", ref movCols, "external_id", "TEXT");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_bank_movements_external ON bank_movements(external_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_bank_movements_operator ON bank_movements(operator_name);");
    }

    private static void EnsureVasilhameTables(SqliteConnection conn)
    {
        EnsureContainerTypesTable(conn);
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS vasilhame_movements (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                customer_id INTEGER,
                borrower_name TEXT,
                borrower_phone TEXT,
                container_type_id INTEGER NOT NULL,
                kind TEXT NOT NULL,
                quantity REAL NOT NULL DEFAULT 1,
                unit_price REAL NOT NULL DEFAULT 0,
                due_date TEXT,
                notes TEXT,
                sale_id INTEGER,
                sale_item_id INTEGER,
                purchase_id INTEGER,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                FOREIGN KEY (container_type_id) REFERENCES container_types(id),
                FOREIGN KEY (customer_id) REFERENCES people(id)
            );
            """);

        // Bancos legados: coluna pode existir como NOT NULL sem DEFAULT.
        var movCols = GetTableColumns(conn, "vasilhame_movements");
        AddColumnIfMissing(conn, "vasilhame_movements", ref movCols, "borrower_name", "TEXT");
        AddColumnIfMissing(conn, "vasilhame_movements", ref movCols, "borrower_phone", "TEXT");
        AddColumnIfMissing(conn, "vasilhame_movements", ref movCols, "due_date", "TEXT");
        AddColumnIfMissing(conn, "vasilhame_movements", ref movCols, "notes", "TEXT");
        AddColumnIfMissing(conn, "vasilhame_movements", ref movCols, "sale_id", "INTEGER");
        AddColumnIfMissing(conn, "vasilhame_movements", ref movCols, "sale_item_id", "INTEGER");
        AddColumnIfMissing(conn, "vasilhame_movements", ref movCols, "purchase_id", "INTEGER");
        AddColumnIfMissing(conn, "vasilhame_movements", ref movCols, "created_at",
            "TEXT NOT NULL DEFAULT (datetime('now','localtime'))");
        if (movCols.Contains("created_at"))
        {
            ExecuteSql(conn, """
                UPDATE vasilhame_movements
                SET created_at = datetime('now','localtime')
                WHERE created_at IS NULL OR TRIM(CAST(created_at AS TEXT)) = '';
                """);
        }

        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_vasilhame_mov_customer ON vasilhame_movements(customer_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_vasilhame_mov_type ON vasilhame_movements(container_type_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_vasilhame_mov_kind ON vasilhame_movements(kind);");
    }

    private static void EnsureAuditLogTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS audit_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                user_login TEXT,
                user_name TEXT,
                action TEXT NOT NULL,
                entity TEXT NOT NULL DEFAULT '',
                entity_id TEXT,
                details TEXT
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_audit_created ON audit_log(created_at);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_audit_action ON audit_log(action);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_audit_user ON audit_log(user_login);");
    }

    private static void EnsurePriceTablesTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS price_tables (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                description TEXT NOT NULL UNIQUE,
                surcharge_percent REAL NOT NULL DEFAULT 0,
                surcharge_fixed REAL NOT NULL DEFAULT 0,
                apply_on_card_only INTEGER NOT NULL DEFAULT 1,
                apply_payment_methods TEXT,
                active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """);
        var cols = GetTableColumns(conn, "price_tables");
        AddColumnIfMissing(conn, "price_tables", ref cols, "surcharge_percent", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "price_tables", ref cols, "surcharge_fixed", "REAL NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "price_tables", ref cols, "apply_on_card_only", "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfMissing(conn, "price_tables", ref cols, "apply_payment_methods", "TEXT");
        AddColumnIfMissing(conn, "price_tables", ref cols, "active", "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfMissing(conn, "price_tables", ref cols, "created_at", "TEXT NOT NULL DEFAULT (datetime('now'))");
    }

    private static void EnsureProductLotsTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS product_lots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                product_id INTEGER NOT NULL,
                lot_number TEXT NOT NULL DEFAULT '',
                expiry_date TEXT,
                quantity REAL NOT NULL DEFAULT 0,
                purchase_id INTEGER,
                unit_cost REAL NOT NULL DEFAULT 0,
                notes TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                FOREIGN KEY (product_id) REFERENCES products(id)
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_product_lots_product ON product_lots(product_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_product_lots_expiry ON product_lots(expiry_date);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_product_lots_purchase ON product_lots(purchase_id);");
    }

    private static void EnsureMovementsTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS movements (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                product_id INTEGER NOT NULL,
                movement_type TEXT NOT NULL,
                quantity REAL NOT NULL,
                unit_price REAL NOT NULL DEFAULT 0,
                supplier_id INTEGER,
                customer_id INTEGER,
                notes TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                FOREIGN KEY (product_id) REFERENCES products(id)
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_movements_product ON movements(product_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_movements_created ON movements(created_at);");

        var cols = GetTableColumns(conn, "movements");
        AddColumnIfMissing(conn, "movements", ref cols, "stock_before", "REAL");
        AddColumnIfMissing(conn, "movements", ref cols, "stock_after", "REAL");
        AddColumnIfMissing(conn, "movements", ref cols, "operation", "TEXT");
        AddColumnIfMissing(conn, "movements", ref cols, "user_name", "TEXT");
        AddColumnIfMissing(conn, "movements", ref cols, "unit", "TEXT");
        AddColumnIfMissing(conn, "movements", ref cols, "ref_type", "TEXT");
        AddColumnIfMissing(conn, "movements", ref cols, "ref_id", "INTEGER");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_movements_ref ON movements(ref_type, ref_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_movements_type ON movements(movement_type);");
    }

    private static void EnsureInventoryTables(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS inventory_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                status TEXT NOT NULL DEFAULT 'aberta',
                group_name TEXT,
                notes TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                closed_at TEXT
            );

            CREATE TABLE IF NOT EXISTS inventory_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL,
                theoretical_qty REAL NOT NULL DEFAULT 0,
                counted_qty REAL,
                notes TEXT,
                FOREIGN KEY (session_id) REFERENCES inventory_sessions(id) ON DELETE CASCADE,
                FOREIGN KEY (product_id) REFERENCES products(id)
            );
            """);
        var itemCols = GetTableColumns(conn, "inventory_items");
        AddColumnIfMissing(conn, "inventory_items", ref itemCols, "counted_at", "TEXT");
        AddColumnIfMissing(conn, "inventory_items", ref itemCols, "count_baseline_qty", "REAL");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_inventory_sessions_status ON inventory_sessions(status);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_inventory_items_session ON inventory_items(session_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_inventory_items_product ON inventory_items(product_id);");
    }

    private static void EnsureProductIndexes(SqliteConnection conn)
    {
        var columns = GetTableColumns(conn, "products");
        if (columns.Count == 0)
            return;

        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_products_name ON products(name);");
        if (columns.Contains("code"))
            ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_products_code ON products(code);");
        if (columns.Contains("barcode"))
            ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_products_barcode ON products(barcode);");
    }

    private static void EnsurePeopleTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS people (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                person_type TEXT NOT NULL DEFAULT 'cliente',
                person_kind TEXT NOT NULL DEFAULT 'juridica',
                name TEXT NOT NULL,
                trade_name TEXT,
                cpf_cnpj TEXT,
                rg_ie TEXT,
                phone TEXT,
                phone2 TEXT,
                cell1 TEXT,
                whatsapp TEXT,
                cell2 TEXT,
                email TEXT,
                cep TEXT,
                address TEXT,
                address_number TEXT,
                complement TEXT,
                neighborhood TEXT,
                city TEXT,
                state TEXT,
                receipt_type TEXT,
                roles_json TEXT NOT NULL DEFAULT '{"ativo":true,"clientes":true}',
                notes TEXT,
                active INTEGER NOT NULL DEFAULT 1,
                fiado_unit_surcharge REAL NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_people_name ON people(name);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_people_cpf_cnpj ON people(cpf_cnpj);");
    }

    private static void EnsurePurchasesTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS purchases (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                supplier_id INTEGER NOT NULL,
                emission_date TEXT NOT NULL,
                entry_date TEXT NOT NULL,
                series TEXT NOT NULL DEFAULT '1',
                number TEXT NOT NULL,
                nfe_key TEXT,
                status TEXT NOT NULL DEFAULT 'aberta',
                total REAL NOT NULL DEFAULT 0,
                gerar_estoque INTEGER NOT NULL DEFAULT 1,
                lot_origin_recorded INTEGER NOT NULL DEFAULT 0,
                notes TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (supplier_id) REFERENCES people(id)
            );

            CREATE TABLE IF NOT EXISTS purchase_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                purchase_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL,
                product_name TEXT NOT NULL,
                quantity REAL NOT NULL,
                unit_price REAL NOT NULL,
                subtotal REAL NOT NULL,
                FOREIGN KEY (purchase_id) REFERENCES purchases(id) ON DELETE CASCADE,
                FOREIGN KEY (product_id) REFERENCES products(id)
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_purchases_status ON purchases(status);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_purchases_number ON purchases(number);");
    }

    /// <summary>
    /// Origem exata do lote recebido em cada item de compra.
    /// Independente do merge em product_lots (mesmo lote/validade).
    /// </summary>
    private static void EnsurePurchaseItemLotsTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS purchase_item_lots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                purchase_item_id INTEGER NOT NULL,
                purchase_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL,
                lot_number TEXT NOT NULL DEFAULT '',
                expiry_date TEXT,
                quantity REAL NOT NULL DEFAULT 0,
                product_lot_id INTEGER,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                FOREIGN KEY (purchase_item_id) REFERENCES purchase_items(id) ON DELETE CASCADE,
                FOREIGN KEY (purchase_id) REFERENCES purchases(id) ON DELETE CASCADE,
                FOREIGN KEY (product_id) REFERENCES products(id),
                FOREIGN KEY (product_lot_id) REFERENCES product_lots(id) ON DELETE SET NULL
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_purchase_item_lots_purchase ON purchase_item_lots(purchase_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_purchase_item_lots_item ON purchase_item_lots(purchase_item_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_purchase_item_lots_product ON purchase_item_lots(product_id);");
    }

    private static void EnsurePayablesTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS payable_titles (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                supplier_id INTEGER NOT NULL,
                purchase_id INTEGER,
                number TEXT NOT NULL,
                emission_date TEXT NOT NULL,
                total_amount REAL NOT NULL DEFAULT 0,
                discount REAL NOT NULL DEFAULT 0,
                interest REAL NOT NULL DEFAULT 0,
                doc_ref TEXT,
                expense_category TEXT DEFAULT 'MERCADORIA',
                notes TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (supplier_id) REFERENCES people(id),
                FOREIGN KEY (purchase_id) REFERENCES purchases(id)
            );

            CREATE TABLE IF NOT EXISTS payable_installments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title_id INTEGER NOT NULL,
                seq INTEGER NOT NULL DEFAULT 1,
                due_date TEXT NOT NULL,
                amount REAL NOT NULL DEFAULT 0,
                discount REAL NOT NULL DEFAULT 0,
                interest REAL NOT NULL DEFAULT 0,
                paid_amount REAL NOT NULL DEFAULT 0,
                paid_date TEXT,
                payment_type TEXT NOT NULL DEFAULT 'Boleto',
                status TEXT NOT NULL DEFAULT 'pendente',
                FOREIGN KEY (title_id) REFERENCES payable_titles(id) ON DELETE CASCADE
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_payable_titles_purchase ON payable_titles(purchase_id);");
    }

    private static void EnsureCashTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS cash_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_date TEXT NOT NULL UNIQUE,
                opening_amount REAL NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'aberta',
                closed_at TEXT,
                counted_amount REAL,
                expected_amount REAL,
                difference_amount REAL,
                notes TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
            );

            CREATE TABLE IF NOT EXISTS cash_movements (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id INTEGER NOT NULL,
                movement_date TEXT NOT NULL,
                kind TEXT NOT NULL,
                description TEXT NOT NULL DEFAULT '',
                party_name TEXT,
                payment_type TEXT,
                amount_in REAL NOT NULL DEFAULT 0,
                amount_out REAL NOT NULL DEFAULT 0,
                affects_balance INTEGER NOT NULL DEFAULT 1,
                ref_type TEXT,
                ref_id INTEGER,
                notes TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                FOREIGN KEY (session_id) REFERENCES cash_sessions(id)
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_cash_movements_session ON cash_movements(session_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_cash_movements_ref ON cash_movements(ref_type, ref_id);");
    }

    private static void EnsureDepositAwaitsTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS deposit_awaits (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                deposit_date TEXT NOT NULL,
                amount REAL NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'pendente',
                confirmed_amount REAL,
                confirmed_at TEXT,
                notes TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_deposit_awaits_status ON deposit_awaits(status);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_deposit_awaits_date ON deposit_awaits(deposit_date);");
    }

    private static void EnsureOpenTabsTables(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS open_tabs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                customer_id INTEGER,
                status TEXT NOT NULL DEFAULT 'open',
                sale_id INTEGER,
                notes TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                settled_at TEXT,
                FOREIGN KEY (customer_id) REFERENCES people(id),
                FOREIGN KEY (sale_id) REFERENCES sales(id)
            );

            CREATE TABLE IF NOT EXISTS open_tab_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tab_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL,
                product_code TEXT NOT NULL DEFAULT '',
                product_name TEXT NOT NULL DEFAULT '',
                unit TEXT NOT NULL DEFAULT 'UN',
                quantity REAL NOT NULL DEFAULT 0,
                unit_price REAL NOT NULL DEFAULT 0,
                subtotal REAL NOT NULL DEFAULT 0,
                stock_units_per_sale REAL NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                FOREIGN KEY (tab_id) REFERENCES open_tabs(id) ON DELETE CASCADE,
                FOREIGN KEY (product_id) REFERENCES products(id)
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_open_tabs_status ON open_tabs(status);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_open_tab_items_tab ON open_tab_items(tab_id);");

        var tabCols = GetTableColumns(conn, "open_tabs");
        AddColumnIfMissing(conn, "open_tabs", ref tabCols, "customer_id", "INTEGER");
        AddColumnIfMissing(conn, "open_tabs", ref tabCols, "sale_id", "INTEGER");
        AddColumnIfMissing(conn, "open_tabs", ref tabCols, "notes", "TEXT");
        AddColumnIfMissing(conn, "open_tabs", ref tabCols, "settled_at", "TEXT");
        AddColumnIfMissing(conn, "open_tabs", ref tabCols, "created_at",
            "TEXT NOT NULL DEFAULT (datetime('now','localtime'))");
        AddColumnIfMissing(conn, "open_tabs", ref tabCols, "preconta_at", "TEXT");
        AddColumnIfMissing(conn, "open_tabs", ref tabCols, "preconta_notify_pending", "INTEGER NOT NULL DEFAULT 0");

        var itemCols = GetTableColumns(conn, "open_tab_items");
        AddColumnIfMissing(conn, "open_tab_items", ref itemCols, "unit", "TEXT NOT NULL DEFAULT 'UN'");
        AddColumnIfMissing(conn, "open_tab_items", ref itemCols, "stock_units_per_sale", "REAL NOT NULL DEFAULT 1");
        AddColumnIfMissing(conn, "open_tab_items", ref itemCols, "created_at",
            "TEXT NOT NULL DEFAULT (datetime('now','localtime'))");
    }

    private static void EnsureSalesTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS sales (
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

            CREATE TABLE IF NOT EXISTS sale_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                sale_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL,
                product_code TEXT NOT NULL DEFAULT '',
                product_name TEXT NOT NULL DEFAULT '',
                unit TEXT NOT NULL DEFAULT 'UN',
                quantity REAL NOT NULL DEFAULT 0,
                unit_price REAL NOT NULL DEFAULT 0,
                subtotal REAL NOT NULL DEFAULT 0,
                FOREIGN KEY (sale_id) REFERENCES sales(id) ON DELETE CASCADE,
                FOREIGN KEY (product_id) REFERENCES products(id)
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_sales_session_date ON sales(session_date);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_sale_items_sale ON sale_items(sale_id);");
    }

    private static void EnsurePixIntentsTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS pix_intents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                sale_id INTEGER,
                mp_payment_id INTEGER NOT NULL UNIQUE,
                idempotency_key TEXT,
                amount REAL NOT NULL DEFAULT 0,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                approved_at TEXT,
                cancelled_at TEXT,
                refunded_at TEXT,
                last_error TEXT
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_pix_intents_sale ON pix_intents(sale_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_pix_intents_status ON pix_intents(status);");
    }

    private static void EnsureSaleExchangeTables(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS sale_exchanges (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                original_sale_id INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                user_id INTEGER,
                user_name TEXT,
                return_total REAL NOT NULL DEFAULT 0,
                new_total REAL NOT NULL DEFAULT 0,
                difference REAL NOT NULL DEFAULT 0,
                payment_type TEXT,
                notes TEXT,
                cash_session_id INTEGER,
                FOREIGN KEY (original_sale_id) REFERENCES sales(id)
            );

            CREATE TABLE IF NOT EXISTS sale_exchange_return_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                exchange_id INTEGER NOT NULL,
                sale_item_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL,
                product_code TEXT NOT NULL DEFAULT '',
                product_name TEXT NOT NULL DEFAULT '',
                qty REAL NOT NULL DEFAULT 0,
                unit_price REAL NOT NULL DEFAULT 0,
                amount REAL NOT NULL DEFAULT 0,
                FOREIGN KEY (exchange_id) REFERENCES sale_exchanges(id)
            );

            CREATE TABLE IF NOT EXISTS sale_exchange_new_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                exchange_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL,
                product_code TEXT NOT NULL DEFAULT '',
                product_name TEXT NOT NULL DEFAULT '',
                unit TEXT NOT NULL DEFAULT 'UN',
                qty REAL NOT NULL DEFAULT 0,
                unit_price REAL NOT NULL DEFAULT 0,
                amount REAL NOT NULL DEFAULT 0,
                FOREIGN KEY (exchange_id) REFERENCES sale_exchanges(id)
            );
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_sale_exchanges_sale ON sale_exchanges(original_sale_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_sale_ex_ret_ex ON sale_exchange_return_items(exchange_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_sale_ex_new_ex ON sale_exchange_new_items(exchange_id);");
    }

    private static void EnsureFiadoTable(SqliteConnection conn)
    {
        ExecuteSql(conn, """
            CREATE TABLE IF NOT EXISTS fiado_payments (
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
            """);
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_fiado_payments_customer ON fiado_payments(customer_id);");
        ExecuteSql(conn, "CREATE INDEX IF NOT EXISTS idx_fiado_payments_date ON fiado_payments(payment_date);");
    }

    private static void SeedDefaultUnits(SqliteConnection conn)
    {
        foreach (var unit in new[] { "UN", "CX", "PCT", "KG", "L", "MC", "CIG", "PC" })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO product_units (name, active) VALUES ($name, 1)
                ON CONFLICT(name) DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("$name", unit);
            cmd.ExecuteNonQuery();
        }
    }

    private static void SeedDefaultAdmin(SqliteConnection conn)
    {
        // Modelo A: sem usuário de fábrica. Se não houver usuários, o App abre o wizard.
        // Instalações antigas que já têm admin/admin continuam; o login força troca de senha.
        _ = conn;
    }
}
