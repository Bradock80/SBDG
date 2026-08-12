using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

public sealed class ContainerTypeInput
{
    public required string Name { get; init; }
    public double SalePrice { get; init; }
    public double Stock { get; init; }
    public string? Notes { get; init; }
    public bool Active { get; init; } = true;
}

public static class ContainerTypesService
{
    public static IReadOnlyList<ContainerType> List(bool onlyActive = false)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.ListContainerTypes(onlyActive);
        return ListLocal(onlyActive);
    }

    public static IReadOnlyList<ContainerType> ListLocal(bool onlyActive = false)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = """
            SELECT id, name, sale_price, stock, active, notes, created_at
            FROM container_types
            """;
        if (onlyActive)
            sql += " WHERE active = 1";
        sql += " ORDER BY name;";
        cmd.CommandText = sql;
        return ReadAll(cmd);
    }

    public static ContainerType? GetById(int id)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.GetContainerType(id);
        return GetByIdLocal(id);
    }

    public static ContainerType? GetByIdLocal(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, sale_price, stock, active, notes, created_at
            FROM container_types WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        return ReadAll(cmd).FirstOrDefault();
    }

    public static ContainerType Create(ContainerTypeInput input)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.CreateContainerType(input);
        return CreateLocal(input);
    }

    public static ContainerType CreateLocal(ContainerTypeInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("criar tipo de vasilhame");
        var name = (input.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Informe o nome do vasilhame.");

        using var conn = DatabaseService.OpenConnection();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM container_types WHERE UPPER(name) = UPPER($n);";
            check.Parameters.AddWithValue("$n", name);
            if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                throw new InvalidOperationException("Já existe um tipo com este nome.");
        }

        using var cmd = conn.CreateCommand();
        // Banco legado exige created_at NOT NULL sem default — preenche sempre.
        cmd.CommandText = """
            INSERT INTO container_types (name, sale_price, stock, notes, active, created_at)
            VALUES ($name, $price, $stock, $notes, $active, datetime('now','localtime'));
            SELECT last_insert_rowid();
            """;
        Bind(cmd, name, input);
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return GetByIdLocal(id) ?? throw new InvalidOperationException("Falha ao criar tipo de vasilhame.");
    }

    public static ContainerType Update(int id, ContainerTypeInput input)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.UpdateContainerType(id, input);
        return UpdateLocal(id, input);
    }

    public static ContainerType UpdateLocal(int id, ContainerTypeInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("atualizar tipo de vasilhame");
        _ = GetByIdLocal(id) ?? throw new InvalidOperationException("Tipo de vasilhame não encontrado.");
        var name = (input.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Informe o nome do vasilhame.");

        using var conn = DatabaseService.OpenConnection();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM container_types WHERE UPPER(name) = UPPER($n) AND id <> $id;";
            check.Parameters.AddWithValue("$n", name);
            check.Parameters.AddWithValue("$id", id);
            if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                throw new InvalidOperationException("Já existe um tipo com este nome.");
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE container_types SET
              name = $name, sale_price = $price, stock = $stock, notes = $notes, active = $active
            WHERE id = $id;
            """;
        Bind(cmd, name, input);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return GetByIdLocal(id)!;
    }

    private static void Bind(SqliteCommand cmd, string name, ContainerTypeInput input)
    {
        cmd.Parameters.AddWithValue("$name", name[..Math.Min(80, name.Length)]);
        cmd.Parameters.AddWithValue("$price", Math.Max(0, input.SalePrice));
        cmd.Parameters.AddWithValue("$stock", Math.Max(0, input.Stock));
        var notes = (input.Notes ?? "").Trim();
        cmd.Parameters.AddWithValue("$notes", string.IsNullOrEmpty(notes) ? DBNull.Value : notes[..Math.Min(500, notes.Length)]);
        cmd.Parameters.AddWithValue("$active", input.Active ? 1 : 0);
    }

    private static List<ContainerType> ReadAll(SqliteCommand cmd)
    {
        var list = new List<ContainerType>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ContainerType
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                SalePrice = reader.GetDouble(2),
                Stock = reader.GetDouble(3),
                Active = reader.GetInt32(4) != 0,
                Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = reader.IsDBNull(6) ? "" : reader.GetString(6),
            });
        }
        return list;
    }
}
