using Microsoft.Data.Sqlite;

namespace SGDB.Services;

/// <summary>
/// Banco da loja (app web) ainda tem tabela <c>suppliers</c> e FKs de
/// <c>purchases</c>/<c>payable_titles</c> apontando para ela, enquanto o nativo
/// usa <c>people</c>. Espelha o fornecedor em <c>suppliers</c> com o mesmo id.
/// </summary>
public static class LegacySupplierBridge
{
    public static void EnsureMirrored(int personId, SqliteConnection? conn = null, SqliteTransaction? tx = null)
    {
        if (personId <= 0)
            return;

        var ownsConn = conn is null;
        conn ??= DatabaseService.OpenConnection();
        try
        {
            if (!TableExists(conn, tx, "suppliers"))
                return;

            // Após LegacySupplierMigration as FKs apontam para people; espelhar
            // em suppliers só sujaria o cadastro legado.
            if (!StillReferencesSuppliers(conn, tx))
                return;

            using (var exists = conn.CreateCommand())
            {
                exists.Transaction = tx;
                exists.CommandText = "SELECT 1 FROM suppliers WHERE id = $id LIMIT 1;";
                exists.Parameters.AddWithValue("$id", personId);
                if (exists.ExecuteScalar() is not null)
                    return;
            }

            using var person = conn.CreateCommand();
            person.Transaction = tx;
            person.CommandText = """
                SELECT name, cpf_cnpj, phone, notes, active, created_at, state
                FROM people WHERE id = $id LIMIT 1;
                """;
            person.Parameters.AddWithValue("$id", personId);
            using var reader = person.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException($"Fornecedor (people #{personId}) não encontrado.");

            var name = reader.IsDBNull(0) ? $"FORNECEDOR {personId}" : reader.GetString(0);
            var cnpj = reader.IsDBNull(1) ? null : reader.GetString(1);
            var phone = reader.IsDBNull(2) ? null : reader.GetString(2);
            var notes = reader.IsDBNull(3) ? null : reader.GetString(3);
            var active = reader.IsDBNull(4) || reader.GetInt32(4) != 0;
            var createdAt = reader.IsDBNull(5) ? null : Convert.ToString(reader.GetValue(5));
            var state = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : null;
            reader.Close();

            var cols = GetColumns(conn, tx, "suppliers");
            using var insert = conn.CreateCommand();
            insert.Transaction = tx;

            if (cols.Contains("state"))
            {
                insert.CommandText = """
                    INSERT INTO suppliers (id, name, cnpj, phone, notes, active, created_at, state)
                    VALUES ($id, $name, $cnpj, $phone, $notes, $active, $created, $state);
                    """;
                insert.Parameters.AddWithValue("$state", (object?)state ?? DBNull.Value);
            }
            else
            {
                insert.CommandText = """
                    INSERT INTO suppliers (id, name, cnpj, phone, notes, active, created_at)
                    VALUES ($id, $name, $cnpj, $phone, $notes, $active, $created);
                    """;
            }

            insert.Parameters.AddWithValue("$id", personId);
            insert.Parameters.AddWithValue("$name", name);
            insert.Parameters.AddWithValue("$cnpj", (object?)cnpj ?? DBNull.Value);
            insert.Parameters.AddWithValue("$phone", (object?)phone ?? DBNull.Value);
            insert.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
            insert.Parameters.AddWithValue("$active", active ? 1 : 0);
            insert.Parameters.AddWithValue("$created",
                string.IsNullOrWhiteSpace(createdAt) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : createdAt);
            insert.ExecuteNonQuery();
        }
        finally
        {
            if (ownsConn)
                conn.Dispose();
        }
    }

    private static bool StillReferencesSuppliers(SqliteConnection conn, SqliteTransaction? tx)
    {
        foreach (var table in new[] { "purchases", "payable_titles" })
        {
            if (!TableExists(conn, tx, table))
                continue;

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"PRAGMA foreign_key_list({table});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader["from"]?.ToString(), "supplier_id", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(reader["table"]?.ToString(), "suppliers", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static bool TableExists(SqliteConnection conn, SqliteTransaction? tx, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n LIMIT 1;";
        cmd.Parameters.AddWithValue("$n", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static HashSet<string> GetColumns(SqliteConnection conn, SqliteTransaction? tx, string table)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            set.Add(reader.GetString(1));
        return set;
    }
}
