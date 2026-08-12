using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SGDB.Services;

/// <summary>
/// Bancos vindos do app web têm <c>purchases.supplier_id</c> e
/// <c>payable_titles.supplier_id</c> apontando para a tabela legada <c>suppliers</c>,
/// mas o nativo mostra o nome via <c>people</c>. Isso fazia as compras aparecerem
/// com o fornecedor trocado (ou em branco).
///
/// Esta migração converte os ids de <c>suppliers</c> para os ids de <c>people</c>
/// equivalentes (casando por CNPJ e, na falta dele, por nome), criando em
/// <c>people</c> os fornecedores que só existiam no cadastro legado, e reconstrói
/// as tabelas para que a FK aponte para <c>people(id)</c>.
/// </summary>
internal static class LegacySupplierMigration
{
    public static void Run(SqliteConnection conn)
    {
        if (!TableExists(conn, "suppliers") || !TableExists(conn, "people"))
            return;

        var purchasesLegacy = TableExists(conn, "purchases")
            && ForeignKeyParent(conn, "purchases", "supplier_id") == "suppliers";
        var titlesLegacy = TableExists(conn, "payable_titles")
            && ForeignKeyParent(conn, "payable_titles", "supplier_id") == "suppliers";

        if (!purchasesLegacy && !titlesLegacy)
            return;

        BackupBeforeMigration(conn);

        var map = BuildSupplierToPersonMap(conn);

        Exec(conn, null, "PRAGMA foreign_keys=OFF;");
        using var tx = conn.BeginTransaction();
        try
        {
            WriteMapTable(conn, tx, map);

            if (purchasesLegacy)
                Rebuild(conn, tx, "purchases", PurchasesSchema, PurchasesColumns, PurchasesExpr);
            if (titlesLegacy)
                Rebuild(conn, tx, "payable_titles", TitlesSchema, TitlesColumns, TitlesExpr);

            Exec(conn, tx, "DROP TABLE IF EXISTS __sup_map;");
            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { /* ignore */ }
            throw;
        }
        finally
        {
            Exec(conn, null, "PRAGMA foreign_keys=ON;");
        }

        Exec(conn, null, "CREATE INDEX IF NOT EXISTS idx_payable_titles_purchase ON payable_titles(purchase_id);");
    }

    /// <summary>Cópia de segurança do banco antes de mexer nas tabelas (uma única vez).</summary>
    private static void BackupBeforeMigration(SqliteConnection conn)
    {
        try
        {
            var dbPath = new SqliteConnectionStringBuilder(conn.ConnectionString).DataSource;
            if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
                return;

            var dir = Path.GetDirectoryName(dbPath);
            if (string.IsNullOrEmpty(dir))
                return;

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dest = Path.Combine(dir, $"deposito_antes_fornecedores_{stamp}.db");
            if (File.Exists(dest))
                return;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO $dest;";
            cmd.Parameters.AddWithValue("$dest", dest);
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // backup é best-effort; não impede a correção
        }
    }

    private static Dictionary<int, int> BuildSupplierToPersonMap(SqliteConnection conn)
    {
        var supplierCols = Columns(conn, "suppliers");
        var people = LoadPeople(conn);
        var byDoc = new Dictionary<string, List<PersonKey>>();
        var byName = new Dictionary<string, List<PersonKey>>();
        foreach (var p in people)
        {
            if (p.Doc.Length >= 11)
            {
                if (!byDoc.TryGetValue(p.Doc, out var listDoc))
                    byDoc[p.Doc] = listDoc = [];
                listDoc.Add(p);
            }
            if (p.NormName.Length > 0)
            {
                if (!byName.TryGetValue(p.NormName, out var listName))
                    byName[p.NormName] = listName = [];
                listName.Add(p);
            }
        }

        var map = new Dictionary<int, int>();
        foreach (var sup in LoadSuppliers(conn, supplierCols))
        {
            var target = Pick(byDoc, sup.Doc.Length >= 11 ? sup.Doc : null)
                ?? Pick(byName, sup.NormName);

            var personId = target?.Id ?? CreatePerson(conn, sup);
            map[sup.Id] = personId;
        }
        return map;
    }

    private static PersonKey? Pick(Dictionary<string, List<PersonKey>> index, string? key)
    {
        if (string.IsNullOrEmpty(key) || !index.TryGetValue(key, out var list) || list.Count == 0)
            return null;
        // prefere quem já está marcado como fornecedor; empate: menor id
        return list
            .OrderByDescending(p => p.IsSupplier)
            .ThenBy(p => p.Id)
            .First();
    }

    private static int CreatePerson(SqliteConnection conn, SupplierRow sup)
    {
        const string roles = """
            {"ativo":true,"clientes":false,"fornecedores":true,"funcionarios":false,"credenciadoras":false,"parceiros":false,"ccf_spc":false,"estrangeiro":false,"marketplaces":false}
            """;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (
                person_type, person_kind, name, cpf_cnpj, phone, state,
                roles_json, notes, active, created_at
            ) VALUES (
                'FORNECEDOR', $kind, $name, $doc, $phone, $state,
                $roles, $notes, $active, datetime('now','localtime')
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$kind", sup.Doc.Length == 11 ? "fisica" : "juridica");
        cmd.Parameters.AddWithValue("$name", sup.Name);
        cmd.Parameters.AddWithValue("$doc", string.IsNullOrEmpty(sup.RawDoc) ? DBNull.Value : sup.RawDoc);
        cmd.Parameters.AddWithValue("$phone", string.IsNullOrEmpty(sup.Phone) ? DBNull.Value : sup.Phone);
        cmd.Parameters.AddWithValue("$state", string.IsNullOrEmpty(sup.State) ? DBNull.Value : sup.State);
        cmd.Parameters.AddWithValue("$roles", roles.Trim());
        cmd.Parameters.AddWithValue("$notes",
            string.IsNullOrEmpty(sup.Notes) ? "Importado do cadastro antigo de fornecedores." : sup.Notes);
        cmd.Parameters.AddWithValue("$active", sup.Active ? 1 : 0);
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void WriteMapTable(SqliteConnection conn, SqliteTransaction tx, Dictionary<int, int> map)
    {
        Exec(conn, tx, "DROP TABLE IF EXISTS __sup_map;");
        Exec(conn, tx, "CREATE TABLE __sup_map (old_id INTEGER PRIMARY KEY, new_id INTEGER NOT NULL);");
        foreach (var (oldId, newId) in map)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO __sup_map (old_id, new_id) VALUES ($o, $n);";
            cmd.Parameters.AddWithValue("$o", oldId);
            cmd.Parameters.AddWithValue("$n", newId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Recria a tabela com a FK correta (people) copiando os dados e traduzindo o
    /// supplier_id pelo mapa. Só copia colunas que existem nas duas versões.
    /// </summary>
    private static void Rebuild(
        SqliteConnection conn,
        SqliteTransaction tx,
        string table,
        string newSchemaTemplate,
        string[] canonicalColumns,
        Func<string, string> expression)
    {
        var tmp = $"{table}__people_fk";
        var oldColumns = Columns(conn, table);
        var copy = canonicalColumns.Where(oldColumns.Contains).ToArray();
        if (copy.Length == 0)
            return;

        Exec(conn, tx, $"DROP TABLE IF EXISTS {tmp};");
        Exec(conn, tx, newSchemaTemplate.Replace("{TABLE}", tmp));

        var cols = string.Join(", ", copy);
        var exprs = string.Join(", ", copy.Select(expression));
        Exec(conn, tx, $"INSERT INTO {tmp} ({cols}) SELECT {exprs} FROM {table};");
        Exec(conn, tx, $"DROP TABLE {table};");
        Exec(conn, tx, $"ALTER TABLE {tmp} RENAME TO {table};");
    }

    private const string MappedSupplier =
        "COALESCE((SELECT m.new_id FROM __sup_map m WHERE m.old_id = supplier_id), supplier_id)";

    private static readonly string[] PurchasesColumns =
    [
        "id", "supplier_id", "emission_date", "entry_date", "series", "number",
        "nfe_key", "status", "total", "gerar_estoque", "notes", "created_at",
    ];

    private const string PurchasesSchema = """
        CREATE TABLE {TABLE} (
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
            notes TEXT,
            created_at TEXT NOT NULL DEFAULT (datetime('now')),
            FOREIGN KEY (supplier_id) REFERENCES people(id)
        );
        """;

    private static string PurchasesExpr(string column) => column switch
    {
        "supplier_id" => MappedSupplier,
        "entry_date" => "COALESCE(entry_date, emission_date, date('now'))",
        "series" => "COALESCE(NULLIF(TRIM(series), ''), '1')",
        "status" => "COALESCE(NULLIF(TRIM(status), ''), 'aberta')",
        "total" => "COALESCE(total, 0)",
        "gerar_estoque" => "COALESCE(gerar_estoque, 1)",
        "created_at" => "COALESCE(NULLIF(TRIM(created_at), ''), datetime('now','localtime'))",
        "number" => "COALESCE(number, '')",
        "emission_date" => "COALESCE(emission_date, date('now'))",
        _ => column,
    };

    private static readonly string[] TitlesColumns =
    [
        "id", "supplier_id", "purchase_id", "number", "emission_date", "total_amount",
        "discount", "interest", "doc_ref", "expense_category", "notes", "created_at",
    ];

    private const string TitlesSchema = """
        CREATE TABLE {TABLE} (
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
        """;

    private static string TitlesExpr(string column) => column switch
    {
        "supplier_id" => MappedSupplier,
        "number" => "COALESCE(number, '')",
        "emission_date" => "COALESCE(emission_date, date('now'))",
        "total_amount" => "COALESCE(total_amount, 0)",
        "discount" => "COALESCE(discount, 0)",
        "interest" => "COALESCE(interest, 0)",
        "created_at" => "COALESCE(NULLIF(TRIM(created_at), ''), datetime('now','localtime'))",
        _ => column,
    };

    private sealed record PersonKey(int Id, string NormName, string Doc, bool IsSupplier);

    private sealed record SupplierRow(
        int Id, string Name, string NormName, string Doc, string RawDoc,
        string Phone, string State, string Notes, bool Active);

    private static List<PersonKey> LoadPeople(SqliteConnection conn)
    {
        var list = new List<PersonKey>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, IFNULL(name, ''), IFNULL(cpf_cnpj, ''), IFNULL(person_type, ''),
                   IFNULL(roles_json, '')
            FROM people;
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(1);
            var roles = reader.GetString(4);
            var isSupplier = reader.GetString(3).Equals("FORNECEDOR", StringComparison.OrdinalIgnoreCase)
                || roles.Replace(" ", "").Contains("\"fornecedores\":true", StringComparison.OrdinalIgnoreCase);
            list.Add(new PersonKey(reader.GetInt32(0), Normalize(name), Digits(reader.GetString(2)), isSupplier));
        }
        return list;
    }

    private static List<SupplierRow> LoadSuppliers(SqliteConnection conn, HashSet<string> cols)
    {
        var hasState = cols.Contains("state");
        var hasNotes = cols.Contains("notes");
        var hasPhone = cols.Contains("phone");
        var hasActive = cols.Contains("active");
        var hasCnpj = cols.Contains("cnpj");

        var select = new StringBuilder("SELECT id, IFNULL(name, '')");
        select.Append(hasCnpj ? ", IFNULL(cnpj, '')" : ", ''");
        select.Append(hasPhone ? ", IFNULL(phone, '')" : ", ''");
        select.Append(hasState ? ", IFNULL(state, '')" : ", ''");
        select.Append(hasNotes ? ", IFNULL(notes, '')" : ", ''");
        select.Append(hasActive ? ", IFNULL(active, 1)" : ", 1");
        select.Append(" FROM suppliers;");

        var list = new List<SupplierRow>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = select.ToString();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            var name = reader.GetString(1);
            if (string.IsNullOrWhiteSpace(name))
                name = $"FORNECEDOR {id}";
            var rawDoc = reader.GetString(2).Trim();
            list.Add(new SupplierRow(
                id,
                name.Trim(),
                Normalize(name),
                Digits(rawDoc),
                rawDoc,
                reader.GetString(3).Trim(),
                reader.GetString(4).Trim(),
                reader.GetString(5).Trim(),
                reader.GetInt32(6) != 0));
        }
        return list;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var decomposed = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Digits(string? value) =>
        string.IsNullOrEmpty(value) ? "" : new string(value.Where(char.IsDigit).ToArray());

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n LIMIT 1;";
        cmd.Parameters.AddWithValue("$n", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static string? ForeignKeyParent(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA foreign_key_list({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader["from"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                return reader["table"]?.ToString();
        }
        return null;
    }

    private static HashSet<string> Columns(SqliteConnection conn, string table)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            set.Add(reader.GetString(1));
        return set;
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction? tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
