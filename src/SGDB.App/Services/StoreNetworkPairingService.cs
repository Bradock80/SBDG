using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SGDB.Services;

/// <summary>
/// ETAPA 68C3-B1 — DeviceId persistente, código temporário e lista de notebooks.
/// O código vive só em memória. Não substitui o pinning TLS da 68C2.
/// </summary>
public static class StoreNetworkPairingService
{
    public const string SettingDeviceId = "store_network_device_id";
    public const string SettingDeviceName = "store_network_device_name";
    public const string SettingDevicesJson = "store_network_devices_json";

    public const int CodeDigits = 8;
    public const int MaxCodeFailures = 3;
    public const int RateLimitMaxFailures = 5;
    public static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(10);

    public const string CorruptDevicesMessage =
        "A lista de computadores da Rede Loja está corrompida e não foi alterada.";
    public const string InvalidDeviceIdStoredMessage =
        "A identidade deste computador está inválida. Não foi gerada uma nova automaticamente.";
    public const string RateLimitedMessage =
        "Muitas tentativas de pareamento. Aguarde alguns minutos.";
    public const string InvalidCodeMessage = "Código de pareamento inválido.";
    public const string ExpiredCodeMessage = "Código de pareamento expirado.";
    public const string ReusedCodeMessage = "Código de pareamento já utilizado.";

    private static readonly object Sync = new();
    private static ActivePairingCode? _active;
    private static readonly Dictionary<string, List<DateTime>> RateFailures = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>B2 pode assinar para derrubar sessões do device revogado.</summary>
    public static event Action<string>? DeviceRevoked;

    internal static Func<DateTime> UtcNow { get; set; } = static () => DateTime.UtcNow;

    public static bool CanGeneratePairingCode() => AccessControl.Can("SistemaUsuarios");

    public static string EnsureDeviceId()
    {
        var raw = (AppSettingsService.GetSetting(SettingDeviceId) ?? "").Trim();
        if (raw.Length == 0)
        {
            var created = Guid.NewGuid().ToString("D");
            AppSettingsService.SetSetting(SettingDeviceId, created);
            return created;
        }

        if (!Guid.TryParse(raw, out var guid) || guid == Guid.Empty)
            throw new InvalidOperationException(InvalidDeviceIdStoredMessage);

        return guid.ToString("D");
    }

    public static string GetDeviceName()
    {
        var saved = (AppSettingsService.GetSetting(SettingDeviceName) ?? "").Trim();
        if (saved.Length > 0)
            return saved;

        var name = SanitizeDeviceName(Environment.MachineName);
        AppSettingsService.SetSetting(SettingDeviceName, name);
        return name;
    }

    public static void SaveDeviceName(string? name)
    {
        AppSettingsService.SetSetting(SettingDeviceName, SanitizeDeviceName(name));
    }

    public static string AbbreviateDeviceId(string? deviceId)
    {
        var id = (deviceId ?? "").Trim();
        if (id.Length <= 8)
            return id;
        return id[..8];
    }

    public static StoreNetworkPairingCode GenerateCode()
    {
        lock (Sync)
        {
            var code = CreateNumericCode();
            _active = new ActivePairingCode(code, UtcNow().Add(CodeTtl));
            return new StoreNetworkPairingCode(code, _active.ExpiresAtUtc);
        }
    }

    public static StoreNetworkPairingCode? PeekActiveCode()
    {
        lock (Sync)
        {
            if (_active is null || UtcNow() >= _active.ExpiresAtUtc)
                return null;
            return new StoreNetworkPairingCode(_active.Code, _active.ExpiresAtUtc);
        }
    }

    public static void ClearActiveCode()
    {
        lock (Sync)
            _active = null;
    }

    public static StoreNetworkPairAttempt TryPair(
        string? pairingCode,
        string? deviceId,
        string? deviceName,
        string? clientIp)
    {
        var ip = string.IsNullOrWhiteSpace(clientIp) ? "unknown" : clientIp.Trim();
        var code = NormalizeCode(pairingCode);
        var name = SanitizeDeviceName(deviceName);

        if (!TryNormalizeDeviceId(deviceId, out var id))
        {
            AuditFailure(id: deviceId, name: name, ip: ip, reason: "invalid_device");
            return Fail(400, "Informe um DeviceId válido.");
        }

        if (code.Length != CodeDigits)
        {
            AuditFailure(id, name, ip, "invalid_code");
            return Fail(400, InvalidCodeMessage);
        }

        lock (Sync)
        {
            if (IsRateLimitedUnlocked(ip))
            {
                AuditFailure(id, name, ip, "rate_limit");
                return Fail(429, RateLimitedMessage);
            }

            var active = _active;
            if (active is null)
            {
                RegisterFailureUnlocked(ip);
                AuditFailure(id, name, ip, "no_code");
                return Fail(401, InvalidCodeMessage);
            }

            if (UtcNow() >= active.ExpiresAtUtc)
            {
                _active = null;
                RegisterFailureUnlocked(ip);
                AuditFailure(id, name, ip, "expired");
                return Fail(401, ExpiredCodeMessage);
            }

            if (!SecureEquals(code, active.Code))
            {
                active.Failures++;
                RegisterFailureUnlocked(ip);
                if (active.Failures >= MaxCodeFailures)
                    _active = null;
                AuditFailure(id, name, ip, "invalid_code");
                return Fail(401, InvalidCodeMessage);
            }

            // Código correto: consumo único antes de persistir.
            _active = null;
            RateFailures.Remove(ip);

            try
            {
                var device = UpsertDeviceUnlocked(id, name);
                AuditService.Log(
                    "rede_pair_success",
                    "store_network",
                    device.DeviceId,
                    FormatAuditDetails(device.DeviceId, device.DeviceName, ip, "ok"));
                return new StoreNetworkPairAttempt
                {
                    StatusCode = 200,
                    Device = device,
                };
            }
            catch (Exception ex)
            {
                AuditFailure(id, name, ip, "persist");
                return Fail(500, ex.Message);
            }
        }
    }

    public static bool IsDeviceAuthorized(string? deviceId)
    {
        if (!TryNormalizeDeviceId(deviceId, out var id))
            return false;
        var device = FindDevice(id);
        return device is { Revoked: false };
    }

    public static StoreNetworkDeviceStatus GetDeviceStatus(string? deviceId, bool touchLastSeen = false)
    {
        if (!TryNormalizeDeviceId(deviceId, out var id))
        {
            return new StoreNetworkDeviceStatus
            {
                DeviceId = (deviceId ?? "").Trim(),
                Authorized = false,
                Revoked = false,
            };
        }

        lock (Sync)
        {
            var list = LoadDevicesUnlocked();
            var device = list.FirstOrDefault(d =>
                string.Equals(d.DeviceId, id, StringComparison.OrdinalIgnoreCase));
            if (device is null)
            {
                return new StoreNetworkDeviceStatus
                {
                    DeviceId = id,
                    Authorized = false,
                    Revoked = false,
                };
            }

            if (touchLastSeen && !device.Revoked)
            {
                device.LastSeenAt = FormatTimestamp(UtcNow());
                SaveDevicesUnlocked(list);
            }

            return new StoreNetworkDeviceStatus
            {
                DeviceId = device.DeviceId,
                DeviceName = device.DeviceName,
                CreatedAt = device.CreatedAt,
                LastSeenAt = device.LastSeenAt,
                Authorized = !device.Revoked,
                Revoked = device.Revoked,
            };
        }
    }

    public static IReadOnlyList<StoreNetworkPairedDevice> ListDevices()
    {
        lock (Sync)
            return LoadDevicesUnlocked();
    }

    public static StoreNetworkPairedDevice Revoke(string? deviceId)
    {
        if (!TryNormalizeDeviceId(deviceId, out var id))
            throw new InvalidOperationException("Informe um DeviceId válido.");

        StoreNetworkPairedDevice revoked;
        lock (Sync)
        {
            var list = LoadDevicesUnlocked();
            var device = list.FirstOrDefault(d =>
                string.Equals(d.DeviceId, id, StringComparison.OrdinalIgnoreCase));
            if (device is null)
                throw new InvalidOperationException("Computador não encontrado.");

            device.Revoked = true;
            SaveDevicesUnlocked(list);
            revoked = device;
        }

        AuditService.Log(
            "rede_device_revoke",
            "store_network",
            revoked.DeviceId,
            FormatAuditDetails(revoked.DeviceId, revoked.DeviceName, null, "revoked"));
        try { DeviceRevoked?.Invoke(revoked.DeviceId); }
        catch { /* B2: não derruba a revogação se o listener falhar */ }
        return revoked;
    }

    internal static void ResetForTests()
    {
        lock (Sync)
        {
            _active = null;
            RateFailures.Clear();
            UtcNow = static () => DateTime.UtcNow;
        }
        DeviceRevoked = null;
    }

    private static StoreNetworkPairedDevice UpsertDeviceUnlocked(string deviceId, string deviceName)
    {
        var list = LoadDevicesUnlocked();
        var existing = list.FirstOrDefault(d =>
            string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        var now = FormatTimestamp(UtcNow());
        if (existing is null)
        {
            existing = new StoreNetworkPairedDevice
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                CreatedAt = now,
                LastSeenAt = now,
                Revoked = false,
            };
            list.Add(existing);
        }
        else
        {
            existing.DeviceName = deviceName;
            existing.LastSeenAt = now;
            existing.Revoked = false;
        }

        SaveDevicesUnlocked(list);
        return existing;
    }

    private static List<StoreNetworkPairedDevice> LoadDevicesUnlocked()
    {
        var raw = AppSettingsService.GetSetting(SettingDevicesJson);
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        try
        {
            var list = JsonSerializer.Deserialize<List<StoreNetworkPairedDevice>>(raw, JsonOpts);
            if (list is null)
                throw new InvalidOperationException(CorruptDevicesMessage);
            return list;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException(CorruptDevicesMessage);
        }
    }

    private static void SaveDevicesUnlocked(List<StoreNetworkPairedDevice> list)
    {
        AppSettingsService.SetSetting(
            SettingDevicesJson,
            JsonSerializer.Serialize(list, JsonOpts));
    }

    private static StoreNetworkPairedDevice? FindDevice(string deviceId)
    {
        lock (Sync)
            return FindDeviceUnlocked(deviceId);
    }

    private static StoreNetworkPairedDevice? FindDeviceUnlocked(string deviceId) =>
        LoadDevicesUnlocked().FirstOrDefault(d =>
            string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));

    private static bool IsRateLimitedUnlocked(string ip)
    {
        PruneFailuresUnlocked(ip);
        return RateFailures.TryGetValue(ip, out var list)
               && list.Count >= RateLimitMaxFailures;
    }

    private static void RegisterFailureUnlocked(string ip)
    {
        PruneFailuresUnlocked(ip);
        if (!RateFailures.TryGetValue(ip, out var list))
        {
            list = [];
            RateFailures[ip] = list;
        }
        list.Add(UtcNow());
    }

    private static void PruneFailuresUnlocked(string ip)
    {
        if (!RateFailures.TryGetValue(ip, out var list))
            return;
        var cutoff = UtcNow() - RateLimitWindow;
        list.RemoveAll(t => t < cutoff);
        if (list.Count == 0)
            RateFailures.Remove(ip);
    }

    private static string CreateNumericCode()
    {
        var n = RandomNumberGenerator.GetInt32(0, 100_000_000);
        return n.ToString("D8");
    }

    private static string NormalizeCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var sb = new StringBuilder(CodeDigits);
        foreach (var c in raw)
        {
            if (char.IsDigit(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    public static bool TryNormalizeDeviceId(string? raw, out string deviceId)
    {
        deviceId = "";
        var trimmed = (raw ?? "").Trim();
        if (!Guid.TryParse(trimmed, out var guid) || guid == Guid.Empty)
            return false;
        deviceId = guid.ToString("D");
        return true;
    }

    public static string SanitizeDeviceName(string? raw)
    {
        var name = (raw ?? "").Trim();
        if (name.Length == 0)
            name = "Notebook";
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (!char.IsControl(c))
                sb.Append(c);
            if (sb.Length >= 80)
                break;
        }
        var clean = sb.ToString().Trim();
        return clean.Length == 0 ? "Notebook" : clean;
    }

    private static string FormatTimestamp(DateTime utc) =>
        utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static bool SecureEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static StoreNetworkPairAttempt Fail(int status, string error) =>
        new() { StatusCode = status, Error = error };

    private static void AuditFailure(string? id, string name, string ip, string reason)
    {
        AuditService.Log(
            "rede_pair_failure",
            "store_network",
            string.IsNullOrWhiteSpace(id) ? null : id.Trim(),
            FormatAuditDetails(id, name, ip, reason));
    }

    private static string FormatAuditDetails(string? deviceId, string? deviceName, string? ip, string result)
    {
        var sb = new StringBuilder();
        sb.Append("result=").Append(result);
        if (!string.IsNullOrWhiteSpace(deviceId))
            sb.Append("; device=").Append(deviceId.Trim());
        if (!string.IsNullOrWhiteSpace(deviceName))
            sb.Append("; name=").Append(SanitizeDeviceName(deviceName));
        if (!string.IsNullOrWhiteSpace(ip))
            sb.Append("; ip=").Append(ip.Trim());
        return sb.ToString();
    }

    private sealed class ActivePairingCode(string code, DateTime expiresAtUtc)
    {
        public string Code { get; } = code;
        public DateTime ExpiresAtUtc { get; } = expiresAtUtc;
        public int Failures { get; set; }
    }
}

public sealed class StoreNetworkPairingCode
{
    public StoreNetworkPairingCode(string code, DateTime expiresAtUtc)
    {
        Code = code;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string Code { get; }
    public DateTime ExpiresAtUtc { get; }
}

public sealed class StoreNetworkPairedDevice
{
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string? LastSeenAt { get; set; }
    public bool Revoked { get; set; }
}

public sealed class StoreNetworkDeviceStatus
{
    public string DeviceId { get; init; } = "";
    public string? DeviceName { get; init; }
    public string? CreatedAt { get; init; }
    public string? LastSeenAt { get; init; }
    public bool Authorized { get; init; }
    public bool Revoked { get; init; }
}

public sealed class StoreNetworkPairAttempt
{
    public int StatusCode { get; init; }
    public string? Error { get; init; }
    public StoreNetworkPairedDevice? Device { get; init; }
    public bool Ok => StatusCode is >= 200 and < 300;
}
