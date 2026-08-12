using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public static class ProductCatalogService
{
    public static string TableName(ProductCatalogKind kind) => kind switch
    {
        ProductCatalogKind.Groups => "product_groups",
        ProductCatalogKind.Units => "product_units",
        ProductCatalogKind.Brands => "product_brands",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string Title(ProductCatalogKind kind) => kind switch
    {
        ProductCatalogKind.Groups => "Grupos",
        ProductCatalogKind.Units => "Unidades",
        ProductCatalogKind.Brands => "Marcas",
        _ => "Catálogo",
    };

    public static string SearchHint(ProductCatalogKind kind) => kind switch
    {
        ProductCatalogKind.Units => "F6 | Pesquisar unidade...",
        ProductCatalogKind.Groups => "F6 | Pesquisar grupo...",
        ProductCatalogKind.Brands => "F6 | Pesquisar marca...",
        _ => "F6 | Pesquisar...",
    };

    public static string NameLabel(ProductCatalogKind kind) =>
        kind == ProductCatalogKind.Units ? "Sigla" : "Nome";

    public static IReadOnlyList<string> ListBrands()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM product_brands WHERE active = 1 ORDER BY name;";
        return ReadNames(cmd);
    }

    public static IReadOnlyList<string> ListGroups()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in ListCatalogNames("product_groups"))
            names.Add(g);

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT group_name FROM products
            WHERE group_name IS NOT NULL AND TRIM(group_name) != '';
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var g = reader.GetString(0).Trim().ToUpperInvariant();
            if (g.Length > 0)
                names.Add(g);
        }

        return names.OrderBy(x => x).ToList();
    }

    public static IReadOnlyList<string> ListUnits()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in ListCatalogNames("product_units"))
            names.Add(u);

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT unit FROM products
            WHERE unit IS NOT NULL AND TRIM(unit) != '';
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var u = reader.GetString(0).Trim().ToUpperInvariant();
            if (u.Length > 0)
                names.Add(u);
        }

        names.Add("UN");
        return names.OrderBy(x => x).ToList();
    }

    public static IReadOnlyList<CatalogItem> ListItems(ProductCatalogKind kind, bool? onlyActive = null, string? search = null)
    {
        var table = TableName(kind);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = $"""
            SELECT id, name, IFNULL(description,''), active, created_at
            FROM {table}
            WHERE 1=1
            """;
        if (onlyActive == true)
            sql += " AND active = 1";
        else if (onlyActive == false)
            sql += " AND active = 0";

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (UPPER(name) LIKE $q OR UPPER(IFNULL(description,'')) LIKE $q)";
            cmd.Parameters.AddWithValue("$q", $"%{search.Trim().ToUpperInvariant()}%");
        }

        sql += " ORDER BY name;";
        cmd.CommandText = sql;

        var list = new List<CatalogItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new CatalogItem
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Active = reader.GetInt32(3) == 1,
                CreatedAt = reader.IsDBNull(4) ? "" : reader.GetString(4),
            });
        }
        return list;
    }

    public static CatalogItem Create(ProductCatalogKind kind, string name, bool active = true, string? description = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("catálogo de produtos");
        var normalized = TextNorm.UpperStr(name)
            ?? throw new InvalidOperationException(
                kind == ProductCatalogKind.Units ? "Informe a sigla da unidade." : "Informe o nome.");
        if (kind == ProductCatalogKind.Units && normalized.Length > 10)
            throw new InvalidOperationException("A sigla deve ter no máximo 10 caracteres.");

        var desc = NormalizeDescription(description);
        var table = TableName(kind);
        using var conn = DatabaseService.OpenConnection();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = $"SELECT COUNT(1) FROM {table} WHERE UPPER(name) = $n;";
            check.Parameters.AddWithValue("$n", normalized);
            if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                throw new InvalidOperationException("Já existe um item com este nome.");
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {table} (name, description, active) VALUES ($name, $description, $active);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", normalized);
        cmd.Parameters.AddWithValue("$description", desc);
        cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return ListItems(kind).First(x => x.Id == id);
    }

    public static CatalogItem Update(ProductCatalogKind kind, int id, string name, bool active, string? description = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("catálogo de produtos");
        var normalized = TextNorm.UpperStr(name)
            ?? throw new InvalidOperationException(
                kind == ProductCatalogKind.Units ? "Informe a sigla da unidade." : "Informe o nome.");
        if (kind == ProductCatalogKind.Units && normalized.Length > 10)
            throw new InvalidOperationException("A sigla deve ter no máximo 10 caracteres.");

        var desc = NormalizeDescription(description);
        var table = TableName(kind);
        using var conn = DatabaseService.OpenConnection();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = $"SELECT COUNT(1) FROM {table} WHERE UPPER(name) = $n AND id <> $id;";
            check.Parameters.AddWithValue("$n", normalized);
            check.Parameters.AddWithValue("$id", id);
            if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                throw new InvalidOperationException("Já existe um item com este nome.");
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {table} SET name = $name, description = $description, active = $active WHERE id = $id;";
        cmd.Parameters.AddWithValue("$name", normalized);
        cmd.Parameters.AddWithValue("$description", desc);
        cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Item não encontrado.");
        return ListItems(kind).First(x => x.Id == id);
    }

    public static void SoftDelete(ProductCatalogKind kind, int id)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("catálogo de produtos");
        var table = TableName(kind);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {table} SET active = 0 WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static void EnsureBrand(string? name)
    {
        var normalized = TextNorm.UpperStr(name);
        if (normalized is null) return;
        StoreNetworkMode.EnsureLocalMutationAllowed("catálogo de produtos");
        UpsertActive("product_brands", normalized);
    }

    public static void EnsureGroup(string? name)
    {
        var normalized = TextNorm.UpperStr(name);
        if (normalized is null) return;
        StoreNetworkMode.EnsureLocalMutationAllowed("catálogo de produtos");
        UpsertActive("product_groups", normalized);
    }

    public static void EnsureUnit(string? name)
    {
        var normalized = TextNorm.UpperStr(name);
        if (normalized is null) return;
        StoreNetworkMode.EnsureLocalMutationAllowed("catálogo de produtos");
        UpsertActive("product_units", normalized);
    }

    private static string NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "";
        var t = description.Trim();
        return t.Length > 80 ? t[..80] : t;
    }

    private static void UpsertActive(string table, string name)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {table} (name, active) VALUES ($name, 1)
            ON CONFLICT(name) DO UPDATE SET active = 1;
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> ListCatalogNames(string table)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT name FROM {table} WHERE active = 1 ORDER BY name;";
        return ReadNames(cmd);
    }

    private static List<string> ReadNames(SqliteCommand cmd)
    {
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(name))
                list.Add(name.Trim().ToUpperInvariant());
        }
        return list;
    }
}
