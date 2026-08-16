using System.IO;
using System.Text;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>ETAPA 68C3-B2 — login remoto, token Bearer e RemoteSession.</summary>
[Collection(TempDatabaseCollection.Name)]
public class StoreNetworkSessionTests
{
    public StoreNetworkSessionTests()
    {
        StoreNetworkPairingService.ResetForTests();
        StoreNetworkSessionService.ResetForTests();
        StoreNetworkClient.ClearSessionState();
    }

    [Fact]
    public void ValidateCredentials_Roles_AndDoesNotTouchAppSession()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var before = AppSession.CurrentUser!.Login;
        CreateUser("adm", "admin", "senha-adm");
        CreateUser("ges", "gestor", "senha-ges");
        CreateUser("ven", "vendedor", "senha-ven");
        CreateUser("off", "vendedor", "senha-off", active: false);

        AssertOk("adm", "senha-adm", "admin");
        AssertOk("ges", "senha-ges", "gestor");
        AssertOk("ven", "senha-ven", "vendedor");

        Assert.Equal(CredentialValidationStatus.InvalidCredentials,
            AuthService.ValidateCredentials("ven", "errada").Status);
        Assert.Equal(CredentialValidationStatus.InvalidCredentials,
            AuthService.ValidateCredentials("naoexiste", "senha-ven").Status);
        Assert.Equal(CredentialValidationStatus.Inactive,
            AuthService.ValidateCredentials("off", "senha-off").Status);

        Assert.Equal(before, AppSession.CurrentUser!.Login);
        var local = AuthService.TryLogin("adm", "senha-adm");
        Assert.NotNull(local);
        Assert.Equal("adm", local!.Login);
        Assert.Equal(before, AppSession.CurrentUser!.Login);
        Assert.Throws<AuthPendingException>(() => AuthService.TryLogin("off", "senha-off"));
        Assert.Null(AuthService.TryLogin("ven", "errada"));
    }

    [Fact]
    public void SessionCreate_RequiresPairedDevice_AndIssues256BitToken()
    {
        using var db = TempDatabase.Create();
        var userId = CreateUser("op", "vendedor", "segredo1");
        var unpaired = StoreNetworkSessionService.TryCreate("op", "segredo1", Guid.NewGuid().ToString("D"), "10.0.0.1");
        Assert.False(unpaired.Ok);
        Assert.Equal(401, unpaired.StatusCode);

        var deviceId = PairDevice("Note-1");
        StoreNetworkPairingService.Revoke(deviceId);
        var revoked = StoreNetworkSessionService.TryCreate("op", "segredo1", deviceId, "10.0.0.1");
        Assert.False(revoked.Ok);

        var device2 = PairDevice("Note-2");
        Assert.False(StoreNetworkSessionService.TryCreate("op", "errada", device2, "10.0.0.1").Ok);
        Assert.False(StoreNetworkSessionService.TryCreate("off", "x", device2, "10.0.0.1").Ok);
        CreateUser("off", "vendedor", "senha-off", active: false);
        Assert.False(StoreNetworkSessionService.TryCreate("off", "senha-off", device2, "10.0.0.1").Ok);
        Assert.Equal(0, StoreNetworkSessionService.SessionCount);

        var a = StoreNetworkSessionService.TryCreate("op", "segredo1", device2, "10.0.0.1");
        var b = StoreNetworkSessionService.TryCreate("op", "segredo1", device2, "10.0.0.1");
        Assert.True(a.Ok);
        Assert.True(b.Ok);
        Assert.NotEqual(a.Session!.Token, b.Session!.Token);
        Assert.Equal(32, StoreNetworkSessionService.FromBase64Url(a.Session.Token).Length);
        Assert.DoesNotContain("=", a.Session.Token);
        Assert.DoesNotContain("+", a.Session.Token);
        Assert.DoesNotContain("/", a.Session.Token);
        Assert.Equal(userId, a.Session.UserId);

        var source = SessionServiceSource();
        Assert.Contains("RandomNumberGenerator.GetBytes(TokenByteLength)", source);
        Assert.Contains("TokenByteLength = 32", source);
        Assert.DoesNotContain("Guid.NewGuid()", source);
        Assert.DoesNotContain("Random.Shared", source);

        Assert.DoesNotContain(a.Session.Token, AppSettingsService.GetSetting(StoreNetworkPairingService.SettingDevicesJson) ?? "");
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(key,'') || '=' || IFNULL(value,'') FROM app_settings;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            Assert.DoesNotContain(a.Session.Token, reader.GetString(0));
    }

    [Fact]
    public void Session_TtlAndRestart()
    {
        using var db = TempDatabase.Create();
        CreateUser("op", "vendedor", "segredo1");
        var deviceId = PairDevice("NB");
        var now = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        StoreNetworkSessionService.UtcNow = () => now;
        var created = StoreNetworkSessionService.TryCreate("op", "segredo1", deviceId, "10.0.0.2");
        Assert.True(created.Ok);
        var token = created.Session!.Token;

        StoreNetworkSessionService.UtcNow = () => now.AddHours(7);
        Assert.True(StoreNetworkSessionService.TryResolve(token).Ok);

        StoreNetworkSessionService.UtcNow = () => now.AddHours(8);
        Assert.False(StoreNetworkSessionService.TryResolve(token).Ok);
        Assert.False(StoreNetworkSessionService.ContainsToken(token));

        StoreNetworkSessionService.UtcNow = () => now;
        var again = StoreNetworkSessionService.TryCreate("op", "segredo1", deviceId, "10.0.0.2");
        Assert.True(again.Ok);
        StoreNetworkSessionService.ClearAll();
        Assert.False(StoreNetworkSessionService.TryResolve(again.Session!.Token).Ok);
    }

    [Fact]
    public void Session_Invalidation_PasswordActiveDevice_AndRevokeHelpers()
    {
        using var db = TempDatabase.Create();
        var userId = CreateUser("op", "vendedor", "segredo1");
        var d1 = PairDevice("D1");
        var d2 = PairDevice("D2");
        var s1 = StoreNetworkSessionService.TryCreate("op", "segredo1", d1, "10.0.0.3");
        var s2 = StoreNetworkSessionService.TryCreate("op", "segredo1", d2, "10.0.0.3");
        Assert.True(s1.Ok && s2.Ok);

        AuthService.ResetPassword("op", "nova-senha");
        Assert.False(StoreNetworkSessionService.TryResolve(s1.Session!.Token).Ok);

        var s3 = StoreNetworkSessionService.TryCreate("op", "nova-senha", d1, "10.0.0.3");
        Assert.True(s3.Ok);
        UsersService.Save(userId, "op", "op", "vendedor", active: false, newPassword: null);
        Assert.False(StoreNetworkSessionService.TryResolve(s3.Session!.Token).Ok);

        var user2 = CreateUser("op2", "vendedor", "segredo2");
        var s4 = StoreNetworkSessionService.TryCreate("op2", "segredo2", d1, "10.0.0.4");
        var s5 = StoreNetworkSessionService.TryCreate("op2", "segredo2", d2, "10.0.0.4");
        StoreNetworkSessionService.RevokeByUser(user2);
        Assert.False(StoreNetworkSessionService.TryResolve(s4.Session!.Token).Ok);
        Assert.False(StoreNetworkSessionService.TryResolve(s5.Session!.Token).Ok);

        UsersService.Save(user2, "op2", "op2", "vendedor", active: true, newPassword: null);
        var s6 = StoreNetworkSessionService.TryCreate("op2", "segredo2", d1, "10.0.0.4");
        var s7 = StoreNetworkSessionService.TryCreate("op2", "segredo2", d2, "10.0.0.4");
        Assert.True(s6.Ok && s7.Ok);
        StoreNetworkPairingService.Revoke(d1);
        Assert.False(StoreNetworkSessionService.TryResolve(s6.Session!.Token).Ok);
        Assert.True(StoreNetworkSessionService.TryResolve(s7.Session!.Token).Ok);
        StoreNetworkSessionService.RevokeByDevice(d2);
        Assert.False(StoreNetworkSessionService.TryResolve(s7.Session!.Token).Ok);
    }

    [Fact]
    public void Logout_RemovesToken_Idempotent()
    {
        using var db = TempDatabase.Create();
        CreateUser("op", "vendedor", "segredo1");
        var deviceId = PairDevice("NB");
        var created = StoreNetworkSessionService.TryCreate("op", "segredo1", deviceId, "10.0.0.5");
        var token = created.Session!.Token;
        Assert.True(StoreNetworkSessionService.Logout(token));
        Assert.False(StoreNetworkSessionService.TryResolve(token).Ok);
        Assert.False(StoreNetworkSessionService.Logout(token));
        Assert.Equal(0, StoreNetworkSessionService.SessionCount);
    }

    [Fact]
    public void AppSession_HostNeverChanges_OnRemoteAuth()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var before = SnapshotSession();
        CreateUser("ven", "vendedor", "senha-ven");
        var deviceId = PairDevice("NB");
        var created = StoreNetworkSessionService.TryCreate("ven", "senha-ven", deviceId, "10.0.0.6");
        Assert.True(created.Ok);
        Assert.Equal("ven", created.Session!.Login);
        AssertEqualSession(before);

        using (AccessControl.EnterRemoteStoreRequest(created.Session))
        {
            Assert.Equal("ven", AccessControl.CurrentRemoteSession!.Login);
            Assert.Equal("admin", AppSession.CurrentUser!.Role);
        }

        StoreNetworkSessionService.Logout(created.Session.Token);
        AssertEqualSession(before);
        Assert.Null(AccessControl.CurrentRemoteSession);

        var src = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "SGDB.App", "Services", "StoreNetworkSessionService.cs")));
        Assert.DoesNotContain("AppSession.SetUser", src);
        Assert.DoesNotContain("AppSession.Clear", src);
        Assert.DoesNotContain("AppSession.RefreshPermissions", AuthServiceSource());
        Assert.DoesNotContain("AppSession.SetUser", AuthServiceSource());
    }

    [Fact]
    public void AsyncLocal_ConcurrentSessions_AndExceptionClears()
    {
        using var db = TempDatabase.Create();
        CreateUser("adm", "admin", "senha-adm");
        CreateUser("ven", "vendedor", "senha-ven");
        var d1 = PairDevice("A");
        var d2 = PairDevice("B");
        var admin = StoreNetworkSessionService.TryCreate("adm", "senha-adm", d1, "10.0.0.7").Session!;
        var vendor = StoreNetworkSessionService.TryCreate("ven", "senha-ven", d2, "10.0.0.8").Session!;

        string? seenA = null;
        string? seenB = null;
        Parallel.Invoke(
            () =>
            {
                using var scope = AccessControl.EnterRemoteStoreRequest(admin);
                Thread.Sleep(40);
                seenA = AccessControl.CurrentRemoteSession!.Login;
            },
            () =>
            {
                using var scope = AccessControl.EnterRemoteStoreRequest(vendor);
                Thread.Sleep(40);
                seenB = AccessControl.CurrentRemoteSession!.Login;
            });
        Assert.Equal("adm", seenA);
        Assert.Equal("ven", seenB);
        Assert.Null(AccessControl.CurrentRemoteSession);

        try
        {
            using (AccessControl.EnterRemoteStoreRequest(admin))
                throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException)
        {
            /* expected */
        }
        Assert.Null(AccessControl.CurrentRemoteSession);

        using (AccessControl.EnterRemoteStoreRequest(vendor))
            Assert.Equal("ven", AccessControl.CurrentRemoteSession!.Login);
        Assert.Null(AccessControl.CurrentRemoteSession);
    }

    [Fact]
    public void Login_RateLimit()
    {
        using var db = TempDatabase.Create();
        CreateUser("op", "vendedor", "segredo1");
        var deviceId = PairDevice("NB");
        var ip = "198.51.100.9";
        for (var i = 0; i < 8; i++)
        {
            var fail = StoreNetworkSessionService.TryCreate("op", "errada", deviceId, ip);
            Assert.Equal(401, fail.StatusCode);
        }
        var blocked = StoreNetworkSessionService.TryCreate("op", "segredo1", deviceId, ip);
        Assert.Equal(429, blocked.StatusCode);
        Assert.DoesNotContain("segredo1", blocked.Error);
        StoreNetworkSessionService.ResetForTests();
        var afterReset = StoreNetworkSessionService.TryCreate("op", "segredo1", deviceId, ip);
        Assert.True(afterReset.Ok);
    }

    [Fact]
    public void Http_LoginRemote_Bearer_PinCompat_AndSecrets()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        ConfigureClient(host, "2468", host.CertificateFingerprint!);
        TestDataHelper.SetSessionRole("admin");
        var hostLogin = AppSession.CurrentUser!.Login;
        CreateUser("ven", "vendedor", "senha-ven");
        try
        {
            var pair = StoreNetworkClient.Pair(StoreNetworkPairingService.GenerateCode().Code);
            Assert.True(pair.Ok);
            var dto = StoreNetworkClient.LoginRemote("ven", "senha-ven");
            Assert.True(dto.Ok);
            Assert.False(string.IsNullOrWhiteSpace(dto.Token));
            Assert.Equal("ven", dto.User!.Login);
            Assert.Equal("ven", StoreNetworkClient.RemoteUser!.Login);
            Assert.Equal(hostLogin, AppSession.CurrentUser!.Login);
            Assert.Equal(32, StoreNetworkSessionService.FromBase64Url(dto.Token!).Length);

            var ping = StoreNetworkClient.Ping();
            Assert.Equal(2, ping.ApiVersion);
            Assert.Contains("session", ping.AuthModes!);
            Assert.Contains("pin", ping.AuthModes!);
            Assert.Equal(TimeSpan.FromSeconds(2), StoreNetworkClient.ConnectTimeout);

            TestDataHelper.SeedSimpleProduct(3, 4, 1, "S68", "Sessao");
            var list = StoreNetworkClient.ListProducts(null, "ativos", null, null, null, "none");
            Assert.Contains(list, p => p.Code == "S68");

            StoreNetworkClient.LogoutRemote();
            Assert.Null(StoreNetworkClient.SessionToken);
            Assert.False(StoreNetworkSessionService.ContainsToken(dto.Token));

            StoreNetworkClient.ClearSessionState();
            var pinOnly = StoreNetworkClient.Login("2468");
            Assert.True(pinOnly.Ok);
            Assert.Contains(StoreNetworkClient.ListProducts(null, "ativos", null, null, null, "none"),
                p => p.Code == "S68");

            Assert.Equal(StoreNetworkClient.SessionNotSupportedMessage,
                "O PC da loja precisa ser atualizado para usar login seguro da Rede Loja.");

            foreach (var row in AuditService.List(limit: 200))
            {
                Assert.DoesNotContain(dto.Token!, row.Details ?? "");
                Assert.DoesNotContain("senha-ven", row.Details ?? "");
                Assert.DoesNotContain("2468", row.Details ?? "");
                Assert.DoesNotContain("password_hash", row.Details ?? "", StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            StoreNetworkClient.ClearSessionState();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void Http_StaleBearerAfterRestart_Is401_PinStillWorksWithoutBearer()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        ConfigureClient(host, "2468", host.CertificateFingerprint!);
        CreateUser("ven", "vendedor", "senha-ven");
        try
        {
            StoreNetworkClient.Pair(StoreNetworkPairingService.GenerateCode().Code);
            var dto = StoreNetworkClient.LoginRemote("ven", "senha-ven");
            StoreNetworkSessionService.ClearAll();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                StoreNetworkClient.ListProducts(null, "ativos", null, null, null, "none"));
            Assert.Contains("Sessão inválida", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(dto.Token!, ex.Message);

            StoreNetworkClient.ClearSessionState();
            Assert.True(StoreNetworkClient.Login("2468").Ok);
        }
        finally
        {
            StoreNetworkClient.ClearSessionState();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void Http_WrongFingerprint_BlocksSession_HttpsRequired()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        StoreNetworkMode.SaveClient("127.0.0.1", "2468", host.Port);
        try
        {
            StoreNetworkMode.ClearServerFingerprint();
            var missing = Assert.Throws<InvalidOperationException>(() =>
                StoreNetworkClient.LoginRemote("a", "b"));
            Assert.Contains("ainda não confia", missing.Message, StringComparison.OrdinalIgnoreCase);

            StoreNetworkMode.SaveServerFingerprint(new string('C', 64));
            var mismatch = Assert.Throws<InvalidOperationException>(() =>
                StoreNetworkClient.LoginRemote("a", "b"));
            Assert.Contains("certificado da loja mudou", mismatch.Message, StringComparison.OrdinalIgnoreCase);

            var clientSrc = File.ReadAllText(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "SGDB.App", "Services", "StoreNetworkClient.cs")));
            Assert.DoesNotContain("DangerousAcceptAny", clientSrc);
            Assert.Contains("MatchesFingerprint", clientSrc);
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    private static void AssertOk(string login, string password, string role)
    {
        var r = AuthService.ValidateCredentials(login, password);
        Assert.Equal(CredentialValidationStatus.Success, r.Status);
        Assert.Equal(role, r.User!.Role);
        Assert.False(string.IsNullOrEmpty(r.PasswordHashFingerprint));
        Assert.Equal(64, r.PasswordHashFingerprint!.Length);
    }

    private static int CreateUser(string login, string role, string password, bool active = true) =>
        UsersService.Save(null, login, login, role, active, password);

    private static string PairDevice(string name)
    {
        var id = Guid.NewGuid().ToString("D");
        var result = StoreNetworkPairingService.TryPair(
            StoreNetworkPairingService.GenerateCode().Code, id, name, "127.0.0.1");
        Assert.True(result.Ok, result.Error);
        return id;
    }

    private static (string? Login, string? Role) SnapshotSession() =>
        (AppSession.CurrentUser?.Login, AppSession.CurrentUser?.Role);

    private static void AssertEqualSession((string? Login, string? Role) before)
    {
        Assert.Equal(before.Login, AppSession.CurrentUser?.Login);
        Assert.Equal(before.Role, AppSession.CurrentUser?.Role);
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

    private static string SessionServiceSource()
    {
        var probe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "SGDB.App", "Services", "StoreNetworkSessionService.cs"));
        Assert.True(File.Exists(probe), probe);
        return File.ReadAllText(probe);
    }

    private static string AuthServiceSource()
    {
        var probe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "SGDB.App", "Services", "AuthService.cs"));
        Assert.True(File.Exists(probe), probe);
        return File.ReadAllText(probe);
    }

    private sealed class CertDirScope : IDisposable
    {
        public string Directory { get; }

        public CertDirScope()
        {
            Directory = Path.Combine(Path.GetTempPath(), "SGDB.Tests.session", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            StoreNetworkCertificateService.OverrideDirectory = Directory;
        }

        public void Dispose()
        {
            if (StoreNetworkCertificateService.OverrideDirectory == Directory)
                StoreNetworkCertificateService.OverrideDirectory = null;
            StoreNetworkHost.Current?.Dispose();
            StoreNetworkPairingService.ResetForTests();
            StoreNetworkSessionService.ResetForTests();
            StoreNetworkClient.ClearSessionState();
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch { /* ignore */ }
        }
    }
}
