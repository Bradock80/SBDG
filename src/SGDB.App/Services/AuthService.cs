using System.Text;
using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

public static class AuthService
{
    public static User? TryLogin(string login, string password)
    {
        var result = ValidateCredentials(login, password);
        return result.Status switch
        {
            CredentialValidationStatus.Success => result.User,
            CredentialValidationStatus.Inactive => throw new AuthPendingException(
                "Sua conta ainda aguarda aprovação do administrador."),
            _ => null,
        };
    }

    /// <summary>
    /// Valida login/senha contra o banco local deste processo, sem tocar AppSession.
    /// No host da Rede Loja, o banco é o da loja.
    /// </summary>
    public static CredentialValidationResult ValidateCredentials(string? login, string? password)
    {
        login = (login ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            return CredentialValidationResult.Invalid();

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
            return CredentialValidationResult.Invalid();

        var hash = reader.GetString(3);
        if (!VerifyPasswordCompatible(password, hash))
            return CredentialValidationResult.Invalid();

        var active = reader.GetInt32(6) != 0;
        var role = reader.GetString(4);
        var user = new User
        {
            Id = reader.GetInt32(0),
            Login = reader.GetString(1),
            Nome = reader.GetString(2),
            Role = role,
            Permissions = UserPermissions.Parse(reader.GetString(5), role),
        };

        if (!active)
            return CredentialValidationResult.InactiveUser(user);

        return CredentialValidationResult.Ok(user, hash);
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

public enum CredentialValidationStatus
{
    InvalidCredentials,
    Inactive,
    Success,
}

public sealed class CredentialValidationResult
{
    public CredentialValidationStatus Status { get; init; }
    public User? User { get; init; }

    /// <summary>SHA-256 do password_hash. Nunca o hash bruto. Só em Success.</summary>
    internal string? PasswordHashFingerprint { get; init; }

    public static CredentialValidationResult Invalid() =>
        new() { Status = CredentialValidationStatus.InvalidCredentials };

    public static CredentialValidationResult InactiveUser(User user) =>
        new() { Status = CredentialValidationStatus.Inactive, User = user };

    public static CredentialValidationResult Ok(User user, string passwordHash) =>
        new()
        {
            Status = CredentialValidationStatus.Success,
            User = user,
            PasswordHashFingerprint = StoreNetworkSessionService.FingerprintPasswordHash(passwordHash),
        };
}

public sealed class AuthPendingException : Exception
{
    public AuthPendingException(string message) : base(message) { }
}
