using System.Text;
using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

public static class AuthService
{
    public static User? TryLogin(string login, string password)
    {
        login = (login ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            return null;

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, login, nome, password_hash, role, IFNULL(permissions_json,''), COALESCE(active, 1)
            FROM users
            WHERE lower(login) = $login
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$login", login);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var hash = reader.GetString(3);
        if (!VerifyPasswordCompatible(password, hash))
            return null;

        var active = reader.GetInt32(6) != 0;
        if (!active)
            throw new AuthPendingException(
                "Sua conta ainda aguarda aprovação do administrador.");

        var role = reader.GetString(4);
        return new User
        {
            Id = reader.GetInt32(0),
            Login = reader.GetString(1),
            Nome = reader.GetString(2),
            Role = role,
            Permissions = UserPermissions.Parse(reader.GetString(5), role),
        };
    }

    /// <summary>
    /// Compatível com hashes do Python (bcrypt $2b$) e BCrypt.Net ($2a$).
    /// Trunca em 72 bytes como o app da loja.
    /// </summary>
    public static bool VerifyPasswordCompatible(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return false;

        var plain = TruncatePasswordBytes(password);
        var candidates = new List<string> { hash.Trim() };
        if (hash.StartsWith("$2b$", StringComparison.OrdinalIgnoreCase))
            candidates.Add("$2a$" + hash[4..]);
        else if (hash.StartsWith("$2a$", StringComparison.OrdinalIgnoreCase))
            candidates.Add("$2b$" + hash[4..]);

        foreach (var h in candidates)
        {
            try
            {
                if (BCrypt.Net.BCrypt.Verify(plain, h))
                    return true;
            }
            catch
            {
                /* tenta próximo formato */
            }
        }

        return false;
    }

    public static string HashPasswordCompatible(string password) =>
        BCrypt.Net.BCrypt.HashPassword(TruncatePasswordBytes(password));

    /// <summary>Redefine senha de um login (para migração / recuperação).</summary>
    public static bool ResetPassword(string login, string newPassword)
    {
        login = (login ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(newPassword))
            return false;

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE users
            SET password_hash = $hash
            WHERE lower(login) = $login;
            """;
        cmd.Parameters.AddWithValue("$login", login);
        cmd.Parameters.AddWithValue("$hash", HashPasswordCompatible(newPassword));
        return cmd.ExecuteNonQuery() > 0;
    }

    private static string TruncatePasswordBytes(string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password ?? "");
        if (bytes.Length <= 72)
            return password ?? "";
        return Encoding.UTF8.GetString(bytes, 0, 72);
    }
}

public sealed class AuthPendingException : Exception
{
    public AuthPendingException(string message) : base(message) { }
}
