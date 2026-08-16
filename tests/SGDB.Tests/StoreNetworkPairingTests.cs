using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>ETAPA 68C3-B1 — DeviceId, código de pareamento e devices persistidos.</summary>
[Collection(TempDatabaseCollection.Name)]
public class StoreNetworkPairingTests
{
    public StoreNetworkPairingTests()
    {
        StoreNetworkPairingService.ResetForTests();
    }

    [Fact]
    public void DeviceId_GeneratesPersistsAndDoesNotUseHostname()
    {
        using var db = TempDatabase.Create();
        var id = StoreNetworkPairingService.EnsureDeviceId();
        Assert.True(Guid.TryParse(id, out var guid) && guid != Guid.Empty);
        Assert.Equal(id, StoreNetworkPairingService.EnsureDeviceId());
        Assert.Equal(id, AppSettingsService.GetSetting(StoreNetworkPairingService.SettingDeviceId));
        Assert.NotEqual(Environment.MachineName, id);
        Assert.False(string.Equals(id, Environment.MachineName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeviceId_InvalidStored_DoesNotRegenerate()
    {
        using var db = TempDatabase.Create();
        AppSettingsService.SetSetting(StoreNetworkPairingService.SettingDeviceId, "nao-e-guid");
        var ex = Assert.Throws<InvalidOperationException>(StoreNetworkPairingService.EnsureDeviceId);
        Assert.Contains("inválida", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("nao-e-guid", AppSettingsService.GetSetting(StoreNetworkPairingService.SettingDeviceId));
    }

    [Fact]
    public void DeviceName_IsDisplayOnly_NotIdentity()
    {
        using var db = TempDatabase.Create();
        var id = StoreNetworkPairingService.EnsureDeviceId();
        var name = StoreNetworkPairingService.GetDeviceName();
        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.NotEqual(id, name);
        StoreNetworkPairingService.SaveDeviceName("Notebook-Sala");
        Assert.Equal("Notebook-Sala", StoreNetworkPairingService.GetDeviceName());
        Assert.Equal(id, StoreNetworkPairingService.EnsureDeviceId());
    }

    [Fact]
    public void PairingCode_IsEightDigits_Rng_SingleUse_NewInvalidatesOld()
    {
        using var db = TempDatabase.Create();
        var first = StoreNetworkPairingService.GenerateCode();
        Assert.Equal(8, first.Code.Length);
        Assert.True(first.Code.All(char.IsDigit));
        Assert.True(first.ExpiresAtUtc > StoreNetworkPairingService.UtcNow());
        Assert.True(first.ExpiresAtUtc <= StoreNetworkPairingService.UtcNow() + StoreNetworkPairingService.CodeTtl);

        var source = PairingServiceSource();
        Assert.Contains("RandomNumberGenerator.GetInt32", source);
        Assert.DoesNotContain("Random.Shared", source);
        Assert.DoesNotContain("new Random(", source);

        var id = Guid.NewGuid().ToString("D");
        var second = StoreNetworkPairingService.GenerateCode();
        Assert.NotEqual(first.Code, second.Code);

        var stale = StoreNetworkPairingService.TryPair(first.Code, id, "A", "10.0.0.1");
        Assert.False(stale.Ok);

        var ok = StoreNetworkPairingService.TryPair(second.Code, id, "A", "10.0.0.1");
        Assert.True(ok.Ok);
        var reused = StoreNetworkPairingService.TryPair(second.Code, Guid.NewGuid().ToString("D"), "B", "10.0.0.2");
        Assert.False(reused.Ok);
        Assert.Equal(StoreNetworkPairingService.InvalidCodeMessage, reused.Error);
    }

    [Fact]
    public void PairingCode_Wrong_Expired_ThreeFailures()
    {
        using var db = TempDatabase.Create();
        var now = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        StoreNetworkPairingService.UtcNow = () => now;
        var code = StoreNetworkPairingService.GenerateCode();

        var wrong = StoreNetworkPairingService.TryPair("00000000", Guid.NewGuid().ToString("D"), "X", "10.1.0.1");
        Assert.False(wrong.Ok);
        Assert.Equal(401, wrong.StatusCode);

        StoreNetworkPairingService.UtcNow = () => now.AddMinutes(6);
        var expired = StoreNetworkPairingService.TryPair(code.Code, Guid.NewGuid().ToString("D"), "X", "10.1.0.1");
        Assert.False(expired.Ok);
        Assert.Equal(StoreNetworkPairingService.ExpiredCodeMessage, expired.Error);

        StoreNetworkPairingService.UtcNow = () => now.AddMinutes(7);
        var fresh = StoreNetworkPairingService.GenerateCode();
        for (var i = 0; i < 3; i++)
        {
            var fail = StoreNetworkPairingService.TryPair("11111111", Guid.NewGuid().ToString("D"), "X", "10.1.0.8");
            Assert.Equal(401, fail.StatusCode);
        }
        var afterLock = StoreNetworkPairingService.TryPair(fresh.Code, Guid.NewGuid().ToString("D"), "X", "10.1.0.8");
        Assert.False(afterLock.Ok);
    }

    [Fact]
    public void Pairing_RateLimit_Returns429()
    {
        using var db = TempDatabase.Create();
        var ip = "203.0.113.9";
        for (var i = 0; i < 5; i++)
        {
            StoreNetworkPairingService.GenerateCode();
            var fail = StoreNetworkPairingService.TryPair("22222222", Guid.NewGuid().ToString("D"), "X", ip);
            Assert.Equal(401, fail.StatusCode);
        }

        var limited = StoreNetworkPairingService.GenerateCode();
        var rate = StoreNetworkPairingService.TryPair(limited.Code, Guid.NewGuid().ToString("D"), "X", ip);
        Assert.Equal(429, rate.StatusCode);
        Assert.Equal(StoreNetworkPairingService.RateLimitedMessage, rate.Error);
    }

    [Fact]
    public void PairingCode_IsNotPersisted_AndNotAudited()
    {
        using var db = TempDatabase.Create();
        var generated = StoreNetworkPairingService.GenerateCode();
        var code = generated.Code;
        var id = Guid.NewGuid().ToString("D");
        Assert.True(StoreNetworkPairingService.TryPair(code, id, "Loja-NB", "10.2.0.1").Ok);
        StoreNetworkPairingService.TryPair(code, Guid.NewGuid().ToString("D"), "Outro", "10.2.0.1");

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(key,'') || '=' || IFNULL(value,'') FROM app_settings;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            Assert.DoesNotContain(code, reader.GetString(0));

        foreach (var row in AuditService.List(limit: 200))
        {
            Assert.DoesNotContain(code, row.Details ?? "");
            Assert.DoesNotContain(code, row.EntityId ?? "");
            Assert.DoesNotContain(code, row.Action ?? "");
        }
    }

    [Fact]
    public void Devices_Persist_Dedupe_Rename_Revoke_AndRepair()
    {
        using var db = TempDatabase.Create();
        var id = Guid.NewGuid().ToString("D");
        var code1 = StoreNetworkPairingService.GenerateCode();
        var first = StoreNetworkPairingService.TryPair(code1.Code, id, "Note-1", "10.3.0.1");
        Assert.True(first.Ok);
        Assert.Contains(id, AppSettingsService.GetSetting(StoreNetworkPairingService.SettingDevicesJson)!);

        var code2 = StoreNetworkPairingService.GenerateCode();
        var again = StoreNetworkPairingService.TryPair(code2.Code, id, "Note-1-novo", "10.3.0.1");
        Assert.True(again.Ok);
        var list = StoreNetworkPairingService.ListDevices();
        Assert.Single(list);
        Assert.Equal("Note-1-novo", list[0].DeviceName);
        Assert.False(list[0].Revoked);

        StoreNetworkPairingService.Revoke(id);
        var revoked = StoreNetworkPairingService.ListDevices().Single();
        Assert.True(revoked.Revoked);
        Assert.False(StoreNetworkPairingService.IsDeviceAuthorized(id));

        var code3 = StoreNetworkPairingService.GenerateCode();
        var repaired = StoreNetworkPairingService.TryPair(code3.Code, id, "Note-1-volta", "10.3.0.1");
        Assert.True(repaired.Ok);
        Assert.True(StoreNetworkPairingService.IsDeviceAuthorized(id));
        Assert.Equal("Note-1-volta", StoreNetworkPairingService.ListDevices().Single().DeviceName);
        Assert.False(StoreNetworkPairingService.ListDevices().Single().Revoked);
    }

    [Fact]
    public void Devices_TwoCoexist_CorruptJsonFailsSafe_ConcurrencyKeepsOneWinner()
    {
        using var db = TempDatabase.Create();
        var a = Guid.NewGuid().ToString("D");
        var b = Guid.NewGuid().ToString("D");
        Assert.True(StoreNetworkPairingService.TryPair(
            StoreNetworkPairingService.GenerateCode().Code, a, "A", "10.4.0.1").Ok);
        Assert.True(StoreNetworkPairingService.TryPair(
            StoreNetworkPairingService.GenerateCode().Code, b, "B", "10.4.0.2").Ok);
        Assert.Equal(2, StoreNetworkPairingService.ListDevices().Count);

        AppSettingsService.SetSetting(StoreNetworkPairingService.SettingDevicesJson, "{nao-json");
        var corrupt = Assert.Throws<InvalidOperationException>(StoreNetworkPairingService.ListDevices);
        Assert.Contains("corrompida", corrupt.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{nao-json", AppSettingsService.GetSetting(StoreNetworkPairingService.SettingDevicesJson));

        using var db2 = TempDatabase.Create();
        StoreNetworkPairingService.ResetForTests();
        var code = StoreNetworkPairingService.GenerateCode().Code;
        var id1 = Guid.NewGuid().ToString("D");
        var id2 = Guid.NewGuid().ToString("D");
        StoreNetworkPairAttempt? r1 = null;
        StoreNetworkPairAttempt? r2 = null;
        Parallel.Invoke(
            () => r1 = StoreNetworkPairingService.TryPair(code, id1, "C1", "10.4.0.3"),
            () => r2 = StoreNetworkPairingService.TryPair(code, id2, "C2", "10.4.0.4"));
        Assert.Equal(1, (r1!.Ok ? 1 : 0) + (r2!.Ok ? 1 : 0));
        Assert.Single(StoreNetworkPairingService.ListDevices());
    }

    [Fact]
    public void LastSeen_UpdatesOnPairAndStatus_NotOnProductApi()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        ConfigureClient(host, "2468", host.CertificateFingerprint!);
        var id = StoreNetworkPairingService.EnsureDeviceId();
        Assert.True(StoreNetworkPairingService.TryPair(
            StoreNetworkPairingService.GenerateCode().Code, id, "NB", "127.0.0.1").Ok);
        var afterPair = StoreNetworkPairingService.ListDevices().Single().LastSeenAt;
        Thread.Sleep(1100);
        TestDataHelper.SeedSimpleProduct(1, 2, 1, "LS1", "LastSeen");
        _ = StoreNetworkClient.ListProducts(null, "ativos", null, null, null, "none");
        Assert.Equal(afterPair, StoreNetworkPairingService.ListDevices().Single().LastSeenAt);

        _ = StoreNetworkClient.GetPairingStatus();
        var afterStatus = StoreNetworkPairingService.ListDevices().Single().LastSeenAt;
        Assert.NotEqual(afterPair, afterStatus);
    }

    [Fact]
    public void HttpPair_RequiresFingerprint_WorksWithTls_PinStillOptional()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        StoreNetworkMode.SaveClient("127.0.0.1", "2468", host.Port);
        StoreNetworkMode.ClearServerFingerprint();
        try
        {
            var missing = Assert.Throws<InvalidOperationException>(() =>
                StoreNetworkClient.Pair(StoreNetworkPairingService.GenerateCode().Code));
            Assert.Contains("ainda não confia", missing.Message, StringComparison.OrdinalIgnoreCase);

            StoreNetworkMode.SaveServerFingerprint(host.CertificateFingerprint!);
            var code = StoreNetworkPairingService.GenerateCode();
            var dto = PairWithoutPin(host, code.Code, StoreNetworkPairingService.EnsureDeviceId(), "SemPin");
            Assert.True(dto.Ok);
            Assert.Equal(StoreNetworkPairingService.EnsureDeviceId(), dto.DeviceId);

            var ping = StoreNetworkClient.Ping();
            Assert.True(ping.Ok);
            Assert.Equal(2, ping.ApiVersion);
            Assert.Contains("pin", ping.AuthModes!);
            Assert.Contains("pairing", ping.AuthModes!);

            StoreNetworkMode.SaveServerFingerprint(new string('C', 64));
            var mismatch = Assert.Throws<InvalidOperationException>(() =>
                StoreNetworkClient.Pair(StoreNetworkPairingService.GenerateCode().Code));
            Assert.Contains("certificado da loja mudou", mismatch.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void HttpPair_WrongPinDoesNotAuthorize_CorrectCodeDoes()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        ConfigureClient(host, "2468", host.CertificateFingerprint!);
        var sessionBefore = AppSession.CurrentUser;
        TestDataHelper.SetSessionRole("vendedor");
        var loginBefore = AppSession.CurrentUser!.Login;
        try
        {
            var code = StoreNetworkPairingService.GenerateCode();
            StoreNetworkMode.SaveClient("127.0.0.1", "9999", host.Port);
            StoreNetworkMode.SaveServerFingerprint(host.CertificateFingerprint!);
            var dto = StoreNetworkClient.Pair(code.Code);
            Assert.True(dto.Ok);
            Assert.False(string.IsNullOrWhiteSpace(dto.CreatedAt));
            Assert.Equal(loginBefore, AppSession.CurrentUser!.Login);
            Assert.False(AccessControl.IsRemoteStoreRequest);

            StoreNetworkMode.SaveClient("127.0.0.1", "2468", host.Port);
            StoreNetworkMode.SaveServerFingerprint(host.CertificateFingerprint!);
            var products = StoreNetworkClient.ListProducts(null, "ativos", null, null, null, "none");
            Assert.NotNull(products);
        }
        finally
        {
            AppSession.SetUser(sessionBefore);
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void Compat_PinRoutesStillWork_WithoutDeviceId_ApiVersion2()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        ConfigureClient(host, "2468", host.CertificateFingerprint!);
        try
        {
            AppSettingsService.SetSetting(StoreNetworkPairingService.SettingDeviceId, "");
            var login = StoreNetworkClient.Login("2468");
            Assert.True(login.Ok);
            var ping = StoreNetworkClient.Ping();
            Assert.Equal(2, ping.ApiVersion);
            TestDataHelper.SeedSimpleProduct(4, 5, 2, "C68", "Compat 68C2");
            var list = StoreNetworkClient.ListProducts(null, "ativos", null, null, null, "none");
            Assert.Contains(list, p => p.Code == "C68");
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void Client_OldServer404_HasClearPairingMessage()
    {
        Assert.Contains("pareamento seguro", StoreNetworkClient.FormatPairHttpError(404), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TimeSpan.FromSeconds(2), StoreNetworkClient.ConnectTimeout);
        var clientSrc = File.ReadAllText(ClientSourcePath());
        Assert.DoesNotContain("DangerousAcceptAny", clientSrc);
        Assert.Contains("MatchesFingerprint", clientSrc);
        Assert.Contains("https://", clientSrc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalPermission_SistemaUsuarios_CanGenerate_VendorCannot()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        Assert.True(StoreNetworkPairingService.CanGeneratePairingCode());
        TestDataHelper.SetSessionRole("gestor");
        Assert.False(StoreNetworkPairingService.CanGeneratePairingCode());
        TestDataHelper.SetSessionRole("vendedor");
        Assert.False(StoreNetworkPairingService.CanGeneratePairingCode());
    }

    [Fact]
    public void RevokedDevice_StatusMessage_AndEventPreparedForB2()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        ConfigureClient(host, "2468", host.CertificateFingerprint!);
        string? revokedId = null;
        StoreNetworkPairingService.DeviceRevoked += id => revokedId = id;
        try
        {
            var id = StoreNetworkPairingService.EnsureDeviceId();
            Assert.True(StoreNetworkPairingService.TryPair(
                StoreNetworkPairingService.GenerateCode().Code, id, "NB", "127.0.0.1").Ok);
            StoreNetworkPairingService.Revoke(id);
            Assert.Equal(id, revokedId);
            var status = StoreNetworkClient.GetPairingStatus();
            Assert.True(status.Revoked);
            Assert.False(status.Authorized);
            Assert.Contains("não está mais autorizado", StoreNetworkClient.DeviceRevokedMessage,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            StoreNetworkPairingService.ResetForTests();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    private static StoreNetworkPairDto PairWithoutPin(
        StoreNetworkHost host, string code, string deviceId, string deviceName)
    {
        var baseUrl = $"https://127.0.0.1:{host.Port}/";
        using var client = StoreNetworkClient.CreateHttpClient(baseUrl, TimeSpan.FromSeconds(20));
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/pair")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { pairingCode = code, deviceId, deviceName }),
                Encoding.UTF8,
                "application/json"),
        };
        var res = client.SendAsync(req).GetAwaiter().GetResult();
        var text = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.True(res.IsSuccessStatusCode, text);
        return JsonSerializer.Deserialize<StoreNetworkPairDto>(text, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        })!;
    }

    private static StoreNetworkHost StartHost(string pin)
    {
        StoreNetworkMode.SavePin(pin);
        return StoreNetworkHost.StartNew(0);
    }

    private static void ConfigureClient(StoreNetworkHost host, string pin, string fingerprint)
    {
        StoreNetworkMode.SaveClient("127.0.0.1", pin, host.Port);
        StoreNetworkMode.SaveServerFingerprint(fingerprint);
    }

    private static string PairingServiceSource()
    {
        var probe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "SGDB.App", "Services", "StoreNetworkPairingService.cs"));
        Assert.True(File.Exists(probe), probe);
        return File.ReadAllText(probe);
    }

    private static string ClientSourcePath()
    {
        var probe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "SGDB.App", "Services", "StoreNetworkClient.cs"));
        Assert.True(File.Exists(probe), probe);
        return probe;
    }

    private sealed class CertDirScope : IDisposable
    {
        public string Directory { get; }

        public CertDirScope()
        {
            Directory = Path.Combine(Path.GetTempPath(), "SGDB.Tests.pair", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            StoreNetworkCertificateService.OverrideDirectory = Directory;
        }

        public void Dispose()
        {
            if (StoreNetworkCertificateService.OverrideDirectory == Directory)
                StoreNetworkCertificateService.OverrideDirectory = null;
            StoreNetworkHost.Current?.Dispose();
            StoreNetworkPairingService.ResetForTests();
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch { /* ignore */ }
        }
    }
}
