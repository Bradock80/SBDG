using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public sealed class ExpenseCategoryInput
{
    public required string Name { get; init; }
    public bool Active { get; init; } = true;
    public int SortOrder { get; init; } = 100;
}

public static class ExpenseCategoriesService
{
    public static void EnsureSeeded(SqliteConnection conn)
    {
        var order = 10;
        foreach (var name in ExpenseCategories.SeedDefaults)
        {
            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(1) FROM expense_categories WHERE UPPER(name) = UPPER($n);";
                check.Parameters.AddWithValue("$n", name);
                if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                {
                    order += 10;
                    continue;
                }
            }

            InsertRow(conn, name, active: true, sortOrder: order, isSystem: true);
            order += 10;
        }
    }

    public static IReadOnlyList<ExpenseCategory> List(string? search = null, string ativo = "ativos")
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = """
            SELECT id, name, active, sort_order, IFNULL(created_at,''), IFNULL(is_system, 0)
            FROM expense_categories
            WHERE 1=1
            """;
        if (ativo == "ativos") sql += " AND active = 1";
        else if (ativo == "inativos") sql += " AND active = 0";

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND UPPER(name) LIKE $like ESCAPE '\\'";
            var escaped = search.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
            cmd.Parameters.AddWithValue("$like", $"%{escaped.ToUpperInvariant()}%");
        }

        sql += " ORDER BY sort_order, name LIMIT 500";
        cmd.CommandText = sql;
        return ReadAll(cmd);
    }

    /// <summary>Nomes ativos para dropdowns (Contas a Pagar / Caixa).</summary>
    public static IReadOnlyList<string> ListActiveNames() =>
        List(ativo: "ativos").Select(c => c.Name).ToList();

    public static ExpenseCategory? GetById(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, active, sort_order, IFNULL(created_at,''), IFNULL(is_system, 0)
            FROM expense_categories WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        return ReadAll(cmd).FirstOrDefault();
    }

    public static ExpenseCategory Create(ExpenseCategoryInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("categorias de despesa");
        var name = NormalizeName(input.Name);
        using var conn = DatabaseService.OpenConnection();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM expense_categories WHERE UPPER(name) = UPPER($n);";
            check.Parameters.AddWithValue("$n", name);
            if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                throw new InvalidOperationException("Já existe uma categoria com este nome.");
        }

        var id = InsertRow(conn, name, input.Active, Math.Clamp(input.SortOrder, 0, 9999), isSystem: false);
        return GetById(id)!;
    }

    public static ExpenseCategory Update(int id, ExpenseCategoryInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("categorias de despesa");
        _ = GetById(id) ?? throw new InvalidOperationException("Categoria não encontrada.");
        var name = NormalizeName(input.Name);

        using var conn = DatabaseService.OpenConnection();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM expense_categories WHERE UPPER(name) = UPPER($n) AND id <> $id;";
            check.Parameters.AddWithValue("$n", name);
            check.Parameters.AddWithValue("$id", id);
            if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                throw new InvalidOperationException("Já existe uma categoria com este nome.");
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE expense_categories SET
              name = $name, active = $active, sort_order = $ord
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$active", input.Active ? 1 : 0);
        cmd.Parameters.AddWithValue("$ord", Math.Clamp(input.SortOrder, 0, 9999));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return GetById(id)!;
    }

    public static void Delete(int id)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("categorias de despesa");
        var existing = GetById(id) ?? throw new InvalidOperationException("Categoria não encontrada.");
        if (existing.IsSystem)
            throw new InvalidOperationException(
                "Categoria padrão do sistema. Em vez de excluir, desative-a ou altere o nome.");

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM expense_categories WHERE id = $id AND IFNULL(is_system,0) = 0;";
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Categoria não encontrada.");
    }

    private static int InsertRow(SqliteConnection conn, string name, bool active, int sortOrder, bool isSystem)
    {
        using var cmd = conn.CreateCommand();
        // Compatível com schema legado (is_system/created_at NOT NULL sem default).
        cmd.CommandText = """
            INSERT INTO expense_categories (name, active, sort_order, is_system, created_at)
            VALUES ($name, $active, $ord, $sys, datetime('now'));
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
        cmd.Parameters.AddWithValue("$ord", sortOrder);
        cmd.Parameters.AddWithValue("$sys", isSystem ? 1 : 0);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string NormalizeName(string? value)
    {
        var name = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Informe o nome da categoria.");
        // Banco legado usa VARCHAR(60).
        if (name.Length > 60)
            name = name[..60];
        return name;
    }

    private static List<ExpenseCategory> ReadAll(SqliteCommand cmd)
    {
        var list = new List<ExpenseCategory>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ExpenseCategory
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Active = Convert.ToInt32(reader.GetValue(2)) != 0,
                SortOrder = reader.IsDBNull(3) ? 100 : Convert.ToInt32(reader.GetValue(3)),
                CreatedAt = reader.IsDBNull(4) ? "" : Convert.ToString(reader.GetValue(4)) ?? "",
                IsSystem = !reader.IsDBNull(5) && Convert.ToInt32(reader.GetValue(5)) != 0,
            });
        }
        return list;
    }
}
