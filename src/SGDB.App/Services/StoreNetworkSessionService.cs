using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// ETAPA 68C3-B2 — sessões remotas em memória (token Bearer 256 bits, TTL 8h).
/// Não persiste. Restart do host apaga tudo.
/// </summary>
public static class StoreNetworkSessionService
{
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(8);
    public const int RateLimitMaxFailures = 8;
    public static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(15);
    public const int TokenByteLength = 32;

    public const string InvalidCredentialsMessage = "Usuário ou senha inválidos.";
    public const string InactiveAccountMessage = "Conta indisponível.";
    public const string DeviceNotAuthorizedMessage =
        "Este computador não está autorizado pela loja.";
    public const string RateLimitedMessage =
        "Muitas tentativas de login. Aguarde alguns minutos.";
    public const string SessionInvalidMessage = "Sessão inválida ou expirada.";

    private static readonly ConcurrentDictionary<string, StoreNetworkRemoteSession> Sessions = new(StringComparer.Ordinal);
    private static readonly object RateLock = new();
    private static readonly Dictionary<string, List<DateTime>> RateFailures = new(StringComparer.Ordinal);

    internal static Func<DateTime> UtcNow { get; set; } = static () => DateTime.UtcNow;

    static StoreNetworkSessionService() => EnsureDeviceRevokedSubscription();

    public static void EnsureDeviceRevokedSubscription()
    {
        StoreNetworkPairingService.DeviceRevoked -= RevokeByDevice;
        StoreNetworkPairingService.DeviceRevoked += RevokeByDevice;
    }

    public static StoreNetworkSessionAttempt TryCreate(
        string? login,
        string? password,
        string? deviceId,
        string? clientIp,
        string? origin = "notebook")
    {
        EnsureDeviceRevokedSubscription();
        var ip = string.IsNullOrWhiteSpace(clientIp) ? "unknown" : clientIp.Trim();
        var normalizedLogin = (login ?? "").Trim().ToLowerInvariant();
        var rateKey = RateKey(ip, normalizedLogin);

        if (string.IsNullOrEmpty(normalizedLogin) || string.IsNullOrEmpty(password))
        {
            AuditLoginFailure(normalizedLogin, deviceId, ip, "invalid");
            return Fail(400, InvalidCredentialsMessage);
        }

        if (!StoreNetworkPairingService.TryNormalizeDeviceId(deviceId, out var id)
            || !StoreNetworkPairingService.IsDeviceAuthorized(id))
        {
            AuditLoginFailure(normalizedLogin, deviceId, ip, "device");
            return Fail(401, DeviceNotAuthorizedMessage);
        }

        lock (RateLock)
        {
            if (IsRateLimitedUnlocked(rateKey))
            {
                AuditLoginFailure(normalizedLogin, id, ip, "rate_limit");
                return Fail(429, RateLimitedMessage);
            }
        }

        var validation = AuthService.ValidateCredentials(normalizedLogin, password);
        if (validation.Status != CredentialValidationStatus.Success
            || validation.User is null
            || string.IsNullOrEmpty(validation.PasswordHashFingerprint))
        {
            lock (RateLock)
                RegisterFailureUnlocked(rateKey);

            if (validation.Status == CredentialValidationStatus.Inactive)
            {
                AuditLoginFailure(normalizedLogin, id, ip, "inactive");
                return Fail(401, InactiveAccountMessage);
            }

            AuditLoginFailure(normalizedLogin, id, ip, "invalid");
            return Fail(401, InvalidCredentialsMessage);
        }

        lock (RateLock)
            RateFailures.Remove(rateKey);

        var now = UtcNow().ToUniversalTime();
        var session = new StoreNetworkRemoteSession
        {
            Token = CreateToken(),
            UserId = validation.User.Id,
            Login = validation.User.Login,
            UserName = validation.User.Nome,
            Role = validation.User.Role,
            Permissions = validation.User.Permissions ?? UserPermissions.ForRole(validation.User.Role),
            DeviceId = id,
            Origin = string.IsNullOrWhiteSpace(origin) ? "notebook" : origin.Trim(),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(Ttl),
            PasswordHashFingerprint = validation.PasswordHashFingerprint,
        };

        if (!Sessions.TryAdd(session.Token, session))
        {
            session = session with { Token = CreateToken() };
            if (!Sessions.TryAdd(session.Token, session))
                return Fail(500, "Não foi possível criar a sessão.");
        }

        var deviceName = StoreNetworkPairingService.GetDeviceStatus(id).DeviceName;
        AuditService.Log(
            "rede_session_login_success",
            "store_network",
            session.Login,
            FormatAudit(session.Login, id, deviceName, ip, "ok"));
        return new StoreNetworkSessionAttempt { StatusCode = 200, Session = session };
    }

    public static StoreNetworkSessionResolution TryResolve(string? token)
    {
        EnsureDeviceRevokedSubscription();
        if (string.IsNullOrWhiteSpace(token))
            return StoreNetworkSessionResolution.Fail();

        if (!Sessions.TryGetValue(token, out var session))
            return StoreNetworkSessionResolution.Fail();

        if (UtcNow().ToUniversalTime() >= session.ExpiresAtUtc)
        {
            Sessions.TryRemove(token, out _);
            return StoreNetworkSessionResolution.Fail();
        }

        if (!StoreNetworkPairingService.IsDeviceAuthorized(session.DeviceId))
        {
            Sessions.TryRemove(token, out _);
            return StoreNetworkSessionResolution.Fail();
        }

        if (!TryReadUserSecurity(session.UserId, out var active, out var hash) || !active)
        {
            Sessions.TryRemove(token, out _);
            return StoreNetworkSessionResolution.Fail();
        }

        var currentFp = FingerprintPasswordHash(hash);
        var sessionFp = session.PasswordHashFingerprint ?? "";
        var expected = Encoding.UTF8.GetBytes(sessionFp);
        var actual = Encoding.UTF8.GetBytes(currentFp);
        if (expected.Length != actual.Length
            || !CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            Sessions.TryRemove(token, out _);
            return StoreNetworkSessionResolution.Fail();
        }

        return new StoreNetworkSessionResolution { Ok = true, Session = session };
    }

    public static bool Logout(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;
        if (!Sessions.TryRemove(token, out var session))
            return false;

        AuditService.Log(
            "rede_session_logout",
            "store_network",
            session.Login,
            FormatAudit(session.Login, session.DeviceId, null, null, "logout"));
        return true;
    }

    public static void RevokeByDevice(string deviceId)
    {
        if (!StoreNetworkPairingService.TryNormalizeDeviceId(deviceId, out var id))
            return;
        foreach (var pair in Sessions)
        {
            if (string.Equals(pair.Value.DeviceId, id, StringComparison.OrdinalIgnoreCase))
                Sessions.TryRemove(pair.Key, out _);
        }
    }

    public static void RevokeByUser(int userId)
    {
        foreach (var pair in Sessions)
        {
            if (pair.Value.UserId == userId)
                Sessions.TryRemove(pair.Key, out _);
        }
    }

    public static void ClearAll() => Sessions.Clear();

    internal static int SessionCount => Sessions.Count;

    internal static bool ContainsToken(string? token) =>
        !string.IsNullOrEmpty(token) && Sessions.ContainsKey(token);

    internal static void ResetForTests()
    {
        Sessions.Clear();
        lock (RateLock)
            RateFailures.Clear();
        UtcNow = static () => DateTime.UtcNow;
        EnsureDeviceRevokedSubscription();
    }

    internal static string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        return ToBase64Url(bytes);
    }

    internal static string FingerprintPasswordHash(string? passwordHash)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash ?? ""));
        return Convert.ToHexString(bytes);
    }

    internal static byte[] FromBase64Url(string token)
    {
        var s = (token ?? "").Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryReadUserSecurity(int userId, out bool active, out string passwordHash)
    {
        active = false;
        passwordHash = "";
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(active, 1), IFNULL(password_hash,'')
            FROM users WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", userId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return false;
        active = reader.GetInt32(0) != 0;
        passwordHash = reader.GetString(1);
        return true;
    }

    private static bool IsRateLimitedUnlocked(string key)
    {
        PruneUnlocked(key);
        return RateFailures.TryGetValue(key, out var list)
               && list.Count >= RateLimitMaxFailures;
    }

    private static void RegisterFailureUnlocked(string key)
    {
        PruneUnlocked(key);
        if (!RateFailures.TryGetValue(key, out var list))
        {
            list = [];
            RateFailures[key] = list;
        }
        list.Add(UtcNow());
    }

    private static void PruneUnlocked(string key)
    {
        if (!RateFailures.TryGetValue(key, out var list))
            return;
        var cutoff = UtcNow() - RateLimitWindow;
        list.RemoveAll(t => t < cutoff);
        if (list.Count == 0)
            RateFailures.Remove(key);
    }

    private static string RateKey(string ip, string login) => ip + "\n" + login;

    private static StoreNetworkSessionAttempt Fail(int status, string error) =>
        new() { StatusCode = status, Error = error };

    private static void AuditLoginFailure(string? login, string? deviceId, string ip, string result)
    {
        AuditService.Log(
            "rede_session_login_failure",
            "store_network",
            string.IsNullOrWhiteSpace(login) ? null : login,
            FormatAudit(login, deviceId, null, ip, result));
    }

    private static string FormatAudit(string? login, string? deviceId, string? deviceName, string? ip, string result)
    {
        var sb = new StringBuilder();
        sb.Append("result=").Append(result);
        if (!string.IsNullOrWhiteSpace(login))
            sb.Append("; login=").Append(login.Trim());
        if (!string.IsNullOrWhiteSpace(deviceId))
            sb.Append("; device=").Append(deviceId.Trim());
        if (!string.IsNullOrWhiteSpace(deviceName))
            sb.Append("; name=").Append(deviceName.Trim());
        if (!string.IsNullOrWhiteSpace(ip))
            sb.Append("; ip=").Append(ip.Trim());
        return sb.ToString();
    }
}

public sealed class StoreNetworkSessionAttempt
{
    public int StatusCode { get; init; }
    public string? Error { get; init; }
    public StoreNetworkRemoteSession? Session { get; init; }
    public bool Ok => StatusCode is >= 200 and < 300;
}

public sealed class StoreNetworkSessionResolution
{
    public bool Ok { get; init; }
    public StoreNetworkRemoteSession? Session { get; init; }
    public static StoreNetworkSessionResolution Fail() => new() { Ok = false };
}
