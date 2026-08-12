using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

public sealed class SellerInput
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Phone { get; init; }
    public string? Cpf { get; init; }
    public double CommissionPercent { get; init; }
    public string? Notes { get; init; }
    public bool Active { get; init; } = true;
}

public static class SellersService
{
    public static IReadOnlyList<Seller> List(string? search = null, string ativo = "ativos")
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = """
            SELECT id, code, name, phone, cpf, commission_percent, notes, active, created_at
            FROM sellers
            WHERE 1=1
            """;
        if (ativo == "ativos") sql += " AND active = 1";
        else if (ativo == "inativos") sql += " AND active = 0";

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += """
                 AND (
                    UPPER(code) LIKE $like ESCAPE '\'
                    OR UPPER(name) LIKE $like ESCAPE '\'
                    OR UPPER(IFNULL(phone,'')) LIKE $like ESCAPE '\'
                 )
                """;
            var escaped = search.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
            cmd.Parameters.AddWithValue("$like", $"%{escaped.ToUpperInvariant()}%");
        }

        sql += " ORDER BY name LIMIT 500";
        cmd.CommandText = sql;
        return ReadAll(cmd);
    }

    public static IReadOnlyList<Seller> ListForPdv() => List(ativo: "ativos");

    public static Seller? GetById(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, code, name, phone, cpf, commission_percent, notes, active, created_at
            FROM sellers WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        return ReadAll(cmd).FirstOrDefault();
    }

    public static Seller Create(SellerInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("vendedores");
        var code = (input.Code ?? "").Trim().ToUpperInvariant();
        var name = (input.Name ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Informe o código do vendedor.");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Informe o nome do vendedor.");

        using var conn = DatabaseService.OpenConnection();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM sellers WHERE UPPER(code) = $c;";
            check.Parameters.AddWithValue("$c", code);
            if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                throw new InvalidOperationException("Já existe vendedor com este código.");
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sellers (code, name, phone, cpf, commission_percent, notes, active)
            VALUES ($code, $name, $phone, $cpf, $comm, $notes, $active);
            SELECT last_insert_rowid();
            """;
        Bind(cmd, code, name, input);
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return GetById(id)!;
    }

    public static Seller Update(int id, SellerInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("vendedores");
        var existing = GetById(id) ?? throw new InvalidOperationException("Vendedor não encontrado.");
        var code = (input.Code ?? "").Trim().ToUpperInvariant();
        var name = (input.Name ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Informe o código do vendedor.");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Informe o nome do vendedor.");

        using var conn = DatabaseService.OpenConnection();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM sellers WHERE UPPER(code) = $c AND id <> $id;";
            check.Parameters.AddWithValue("$c", code);
            check.Parameters.AddWithValue("$id", id);
            if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                throw new InvalidOperationException("Já existe vendedor com este código.");
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE sellers SET
              code = $code, name = $name, phone = $phone, cpf = $cpf,
              commission_percent = $comm, notes = $notes, active = $active
            WHERE id = $id;
            """;
        Bind(cmd, code, name, input);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return GetById(id)!;
    }

    public static void Delete(int id)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("vendedores");
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sellers WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Vendedor não encontrado.");
    }

    public static int? ResolveActiveId(int? sellerId)
    {
        if (sellerId is null or <= 0)
            return null;
        var s = GetById(sellerId.Value);
        if (s is null || !s.Active)
            throw new InvalidOperationException("Vendedor inválido ou inativo.");
        return s.Id;
    }

    private static void Bind(SqliteCommand cmd, string code, string name, SellerInput input)
    {
        cmd.Parameters.AddWithValue("$code", code[..Math.Min(20, code.Length)]);
        cmd.Parameters.AddWithValue("$name", name[..Math.Min(120, name.Length)]);
        cmd.Parameters.AddWithValue("$phone", (object?)NullIfEmpty(input.Phone, 30) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cpf", (object?)NullIfEmpty(input.Cpf, 14) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$comm", Math.Round(Math.Clamp(input.CommissionPercent, 0, 100), 4));
        cmd.Parameters.AddWithValue("$notes", (object?)NullIfEmpty(input.Notes, 500) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$active", input.Active ? 1 : 0);
    }

    private static string? NullIfEmpty(string? s, int max)
    {
        var t = (s ?? "").Trim();
        return string.IsNullOrEmpty(t) ? null : t[..Math.Min(max, t.Length)];
    }

    private static List<Seller> ReadAll(SqliteCommand cmd)
    {
        var list = new List<Seller>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Seller
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                Phone = reader.IsDBNull(3) ? null : reader.GetString(3),
                Cpf = reader.IsDBNull(4) ? null : reader.GetString(4),
                CommissionPercent = reader.GetDouble(5),
                Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
                Active = reader.GetInt32(7) != 0,
                CreatedAt = reader.IsDBNull(8) ? "" : reader.GetString(8),
            });
        }
        return list;
    }
}
