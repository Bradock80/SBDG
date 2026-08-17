using System.Diagnostics;
using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>ETAPA 68C3-B3 — login remoto como fluxo normal do notebook.</summary>
[Collection(TempDatabaseCollection.Name)]
public class StoreNetworkClientLoginTests
{
    public StoreNetworkClientLoginTests()
    {
        StoreNetworkPairingService.ResetForTests();
        StoreNetworkSessionService.ResetForTests();
        StoreNetworkClient.ClearSessionState();
        ApplicationLoginService.ResetForTests();
        AppSession.Clear();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
    }

    [Fact]
    public void Local_StandaloneAndServer_UseAuthService()
    {
        using var db = TempDatabase.Create();
        ApplicationLoginService.RemoteLoginOverride = (_, _) =>
            throw new InvalidOperationException("remote não deveria ser chamado");
        CreateUser("adm", "admin", "senha-adm");
        CreateUser("ges", "gestor", "senha-ges");
        CreateUser("ven", "vendedor", "senha-ven");

        foreach (var role in new[] { StoreNetworkMode.RoleStandalone, StoreNetworkMode.RoleServer })
        {
            StoreNetworkMode.SetRole(role);
            Assert.False(ApplicationLoginService.IsRemoteLogin);
            AssertOk("adm", "senha-adm", "admin");
            AssertOk("ges", "senha-ges", "gestor");
            AssertOk("ven", "senha-ven", "vendedor");
            var bad = ApplicationLoginService.TryLogin("ven", "errada");
            Assert.False(bad.Ok);
            Assert.Equal(ApplicationLoginService.InvalidCredentialsMessage, bad.Error);
            Assert.Null(AppSession.CurrentUser);
        }
    }

    [Fact]
    public void Local_InitialSetupRegisterPasswordChange_Preserved()
    {
        using var db = TempDatabase.Create();
        Assert.True(ApplicationLoginService.ShouldRunInitialSetup());
        Assert.Equal(0, SetupService.CountUsers());

        var recovery = SetupService.CompleteInitialSetup(
            new CompanyProfile { NomeFantasia = "Loja Teste" },
            "adminloja",
            "Admin Loja",
            "senha-forte");
        Assert.False(string.IsNullOrWhiteSpace(recovery));
        Assert.False(ApplicationLoginService.ShouldRunInitialSetup());
        AssertOk("adminloja", "senha-forte", "admin");

        var id = UsersService.RegisterSelf("Vendedor Novo", "vendnovo", null, "senha-ven");
        Assert.True(id > 0);
        Assert.Throws<AuthPendingException>(() => AuthService.TryLogin("vendnovo", "senha-ven"));

        Assert.True(AuthService.ResetPassword("adminloja", "outra-senha"));
        AssertOk("adminloja", "outra-senha", "admin");
        ApplicationLoginService.EnsureLocalPasswordChange();
    }

    [Fact]
    public void Client_DoesNotUseLocalAuth_AndFillsAppSessionFromServer()
    {
        using var db = TempDatabase.Create();
        SetClient();
        var localId = CreateUser("ven", "admin", "senha-local-admin");
        CreateUser("outro", "vendedor", "senha-local-ven");
        var remotePerms = UserPermissions.ForRole("vendedor");
        var remoteCalls = 0;
        ApplicationLoginService.RemoteLoginOverride = (login, password) =>
        {
            remoteCalls++;
            Assert.Equal("ven", login);
            Assert.Equal("segredo-servidor", password);
            return new StoreNetworkSessionDto
            {
                Ok = true,
                Token = "token-teste",
                User = new StoreNetworkRemoteUserDto
                {
                    Id = 9001,
                    Login = "ven",
                    Name = "Vendedor Loja",
                    Role = "vendedor",
                    Permissions = remotePerms,
                },
            };
        };

        var result = ApplicationLoginService.TryLogin("ven", "segredo-servidor");
        Assert.True(result.Ok);
        Assert.True(result.UsedRemoteLogin);
        Assert.Equal(1, remoteCalls);
        Assert.Equal(9001, AppSession.CurrentUser!.Id);
        Assert.Equal("ven", AppSession.CurrentUser.Login);
        Assert.Equal("Vendedor Loja", AppSession.CurrentUser.Nome);
        Assert.Equal("vendedor", AppSession.CurrentUser.Role);
        Assert.False(AppSession.Permissions.SistemaUsuarios);
        Assert.True(AppSession.Permissions.PdvVenda);
        Assert.NotEqual(localId, AppSession.CurrentUser.Id);
        Assert.Null(result.TypedPassword);
    }

    [Fact]
    public void Client_LocalVendorDoesNotBecomeAdmin_AndInverse()
    {
        using var db = TempDatabase.Create();
        SetClient();
        CreateUser("op", "vendedor", "local-ven");
        CreateUser("adm", "admin", "local-adm");

        ApplicationLoginService.RemoteLoginOverride = (_, _) => RemoteDto(42, "op", "admin", UserPermissions.ForRole("admin"));
        var admin = ApplicationLoginService.TryLogin("op", "x");
        Assert.True(admin.Ok);
        Assert.Equal("admin", AppSession.CurrentUser!.Role);
        Assert.True(AppSession.Permissions.SistemaUsuarios);
        Assert.Equal(42, AppSession.CurrentUser.Id);

        ApplicationLoginService.RemoteLoginOverride = (_, _) => RemoteDto(7, "adm", "vendedor", UserPermissions.ForRole("vendedor"));
        var vendor = ApplicationLoginService.TryLogin("adm", "x");
        Assert.True(vendor.Ok);
        Assert.Equal("vendedor", AppSession.CurrentUser!.Role);
        Assert.False(AppSession.Permissions.SistemaUsuarios);
        Assert.Equal(7, AppSession.CurrentUser.Id);
    }

    [Fact]
    public void Client_MissingFingerprint_WrongFingerprint_NoSilentTofu()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        StoreNetworkMode.SaveClient("127.0.0.1", "2468", host.Port);
        try
        {
            StoreNetworkMode.ClearServerFingerprint();
            var missing = ApplicationLoginService.TryLogin("a", "b");
            Assert.False(missing.Ok);
            Assert.Equal(ApplicationLoginService.MissingFingerprintMessage, missing.Error);
            Assert.True(missing.CanOpenStoreNetworkSettings);
            Assert.Null(AppSession.CurrentUser);

            StoreNetworkMode.SaveServerFingerprint(new string('C', 64));
            var mismatch = ApplicationLoginService.TryLogin("a", "b");
            Assert.False(mismatch.Ok);
            Assert.Equal(ApplicationLoginService.FingerprintMismatchMessage, mismatch.Error);
            Assert.Null(AppSession.CurrentUser);
            Assert.True(StoreNetworkMode.IsClient);
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void Client_InvalidDeviceId_DoesNotRegenerate()
    {
        using var db = TempDatabase.Create();
        SetClient();
        StoreNetworkMode.SaveServerFingerprint(new string('A', 64));
        AppSettingsService.SetSetting(StoreNetworkPairingService.SettingDeviceId, "nao-e-guid");
        var result = ApplicationLoginService.TryLogin("a", "b");
        Assert.False(result.Ok);
        Assert.Equal(StoreNetworkPairingService.InvalidDeviceIdStoredMessage, result.Error);
        Assert.Equal("nao-e-guid", AppSettingsService.GetSetting(StoreNetworkPairingService.SettingDeviceId));
        Assert.Null(AppSession.CurrentUser);
    }

    [Fact]
    public void Client_Offline_FailClosed_Fast()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SaveClient("192.0.2.1", "1234", StoreNetworkMode.DefaultPort);
        StoreNetworkMode.SaveServerFingerprint(new string('A', 64));
        try
        {
            Assert.True(StoreNetworkMode.IsClient);
            var sw = Stopwatch.StartNew();
            var result = ApplicationLoginService.TryLogin("ven", "senha");
            sw.Stop();
            Assert.False(result.Ok);
            Assert.Equal(ApplicationLoginService.ServerOfflineMessage, result.Error);
            Assert.Null(AppSession.CurrentUser);
            Assert.True(StoreNetworkMode.IsClient);
            Assert.Equal(StoreNetworkMode.RoleClient, StoreNetworkMode.GetRole());
            Assert.Equal(TimeSpan.FromSeconds(2), StoreNetworkClient.ConnectTimeout);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
                $"offline levou {sw.Elapsed.TotalSeconds:N2}s");
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void Client_Http_PairingAndLoginAndLogout()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        ConfigureClient(host, "2468", host.CertificateFingerprint!);
        CreateUser("ven", "vendedor", "senha-ven");
        CreateUser("adm", "admin", "senha-adm");
        try
        {
            var unpaired = ApplicationLoginService.TryLogin("ven", "senha-ven");
            Assert.False(unpaired.Ok);
            Assert.Equal(ApplicationLoginService.DeviceNotPairedMessage, unpaired.Error);
            Assert.Null(AppSession.CurrentUser);

            var pair = StoreNetworkClient.Pair(StoreNetworkPairingService.GenerateCode().Code);
            Assert.True(pair.Ok);
            var deviceId = pair.DeviceId!;

            var bad = ApplicationLoginService.TryLogin("ven", "errada");
            Assert.False(bad.Ok);
            Assert.Equal(ApplicationLoginService.InvalidCredentialsMessage, bad.Error);
            Assert.Null(AppSession.CurrentUser);

            CreateUser("off", "vendedor", "senha-off", active: false);
            var inactive = ApplicationLoginService.TryLogin("off", "senha-off");
            Assert.False(inactive.Ok);
            Assert.Equal(ApplicationLoginService.InactiveAccountMessage, inactive.Error);

            var ok = ApplicationLoginService.TryLogin("ven", "senha-ven");
            Assert.True(ok.Ok);
            Assert.True(ok.UsedRemoteLogin);
            Assert.Equal("ven", AppSession.CurrentUser!.Login);
            Assert.Equal("vendedor", AppSession.CurrentUser.Role);
            Assert.False(string.IsNullOrWhiteSpace(StoreNetworkClient.SessionToken));
            Assert.Equal("ven", StoreNetworkClient.RemoteUser!.Login);
            Assert.False(AppSession.Permissions.SistemaUsuarios);

            ApplicationLoginService.Logout();
            Assert.Null(StoreNetworkClient.SessionToken);
            Assert.Null(StoreNetworkClient.RemoteUser);
            Assert.Null(AppSession.CurrentUser);
            Assert.True(StoreNetworkMode.IsClient);

            var again = ApplicationLoginService.TryLogin("adm", "senha-adm");
            Assert.True(again.Ok);
            Assert.Equal("admin", AppSession.CurrentUser!.Role);
            Assert.True(AppSession.Permissions.SistemaUsuarios);

            StoreNetworkPairingService.Revoke(deviceId);
            var revoked = ApplicationLoginService.TryLogin("adm", "senha-adm");
            Assert.False(revoked.Ok);
            Assert.Equal(ApplicationLoginService.DeviceRevokedMessage, revoked.Error);
        }
        finally
        {
            StoreNetworkClient.ClearSessionState();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void Client_Http_LogoutOfflineStillClearsLocal()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        ConfigureClient(host, "2468", host.CertificateFingerprint!);
        CreateUser("ven", "vendedor", "senha-ven");
        try
        {
            StoreNetworkClient.Pair(StoreNetworkPairingService.GenerateCode().Code);
            Assert.True(ApplicationLoginService.TryLogin("ven", "senha-ven").Ok);
            Assert.NotNull(StoreNetworkClient.SessionToken);
            host.Dispose();

            ApplicationLoginService.Logout();
            Assert.Null(StoreNetworkClient.SessionToken);
            Assert.Null(StoreNetworkClient.RemoteUser);
            Assert.Null(AppSession.CurrentUser);
        }
        finally
        {
            StoreNetworkClient.ClearSessionState();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void Client_Http_401ClearsNotebookSession_NoLocalFallback_NoLoop()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        ConfigureClient(host, "2468", host.CertificateFingerprint!);
        var userId = CreateUser("ven", "vendedor", "senha-ven");
        try
        {
            StoreNetworkClient.Pair(StoreNetworkPairingService.GenerateCode().Code);
            Assert.True(ApplicationLoginService.TryLogin("ven", "senha-ven").Ok);
            StoreNetworkSessionService.ClearAll();
            var expired = Assert.Throws<StoreNetworkSessionExpiredException>(() =>
                StoreNetworkClient.ListProducts(null, "ativos", null, null, null, "none"));
            Assert.Equal(ApplicationLoginService.SessionExpiredMessage, expired.Message);
            Assert.Null(AppSession.CurrentUser);
            Assert.Null(StoreNetworkClient.SessionToken);
            Assert.Equal(1, ApplicationLoginService.SessionExpiredNotifications);

            Assert.Throws<StoreNetworkSessionExpiredException>(() =>
                ApplicationLoginService.HandleAuthenticatedRequestUnauthorized(
                    StoreNetworkSessionService.SessionInvalidMessage));
            Assert.Equal(1, ApplicationLoginService.SessionExpiredNotifications);

            ApplicationLoginService.ResetForTests();
            StoreNetworkClient.Pair(StoreNetworkPairingService.GenerateCode().Code);
            Assert.True(ApplicationLoginService.TryLogin("ven", "senha-ven").Ok);
            AuthService.ResetPassword("ven", "nova-senha");
            var afterPassword = Assert.Throws<StoreNetworkSessionExpiredException>(() =>
                StoreNetworkClient.ListProducts(null, "ativos", null, null, null, "none"));
            Assert.Equal(ApplicationLoginService.SessionExpiredMessage, afterPassword.Message);
            Assert.Null(AppSession.CurrentUser);

            StoreNetworkClient.Pair(StoreNetworkPairingService.GenerateCode().Code);
            Assert.True(ApplicationLoginService.TryLogin("ven", "nova-senha").Ok);
            SeedHostUser(() => UsersService.Save(userId, "ven", "ven", "vendedor", active: false, newPassword: null));
            var afterInactive = Assert.Throws<StoreNetworkSessionExpiredException>(() =>
                StoreNetworkClient.ListProducts(null, "ativos", null, null, null, "none"));
            Assert.Null(AppSession.CurrentUser);

            SeedHostUser(() => UsersService.Save(userId, "ven", "ven", "vendedor", active: true, newPassword: null));
            var deviceId = StoreNetworkPairingService.EnsureDeviceId();
            StoreNetworkClient.Pair(StoreNetworkPairingService.GenerateCode().Code);
            Assert.True(ApplicationLoginService.TryLogin("ven", "nova-senha").Ok);
            StoreNetworkPairingService.Revoke(deviceId);
            var afterRevoke = Assert.Throws<StoreNetworkSessionExpiredException>(() =>
                StoreNetworkClient.ListProducts(null, "ativos", null, null, null, "none"));
            Assert.Null(AppSession.CurrentUser);

            ApplicationLoginService.RemoteLoginOverride = (_, _) =>
                throw new InvalidOperationException(ApplicationLoginService.InvalidCredentialsMessage);
            var before = ApplicationLoginService.SessionExpiredNotifications;
            var invalid = ApplicationLoginService.TryLogin("ven", "errada");
            Assert.False(invalid.Ok);
            Assert.Equal(ApplicationLoginService.InvalidCredentialsMessage, invalid.Error);
            Assert.Equal(before, ApplicationLoginService.SessionExpiredNotifications);
        }
        finally
        {
            StoreNetworkClient.ClearSessionState();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void Client_BlocksLocalUserAdmin_AndDoesNotPersistSession()
    {
        using var db = TempDatabase.Create();
        SetClient();
        Assert.False(ApplicationLoginService.ShouldRunInitialSetup());
        Assert.Equal(0, SetupService.CountUsers());
        var setup = Assert.Throws<InvalidOperationException>(() =>
            SetupService.CompleteInitialSetup(
                new CompanyProfile { NomeFantasia = "Notebook" },
                "admin",
                "Admin",
                "senha-forte"));
        Assert.Equal(ApplicationLoginService.RegisterOnServerMessage, setup.Message);
        Assert.Equal(0, SetupService.CountUsers());

        var register = Assert.Throws<InvalidOperationException>(() =>
            UsersService.RegisterSelf("Nome", "loginx", null, "senha1"));
        Assert.Equal(ApplicationLoginService.RegisterOnServerMessage, register.Message);
        Assert.Equal(0, SetupService.CountUsers());

        var pwd = Assert.Throws<InvalidOperationException>(
            ApplicationLoginService.EnsureLocalPasswordChange);
        Assert.Equal(ApplicationLoginService.PasswordChangeUnavailableMessage, pwd.Message);

        Assert.False(SetupService.TryResetPasswordWithRecovery("admin", "XXXX", "nova", out var err));
        Assert.Equal(ApplicationLoginService.PasswordChangeUnavailableMessage, err);

        Assert.True(StoreNetworkMode.IsModuleBlockedOnClient("usuarios"));
        Assert.Equal(
            ApplicationLoginService.LocalUserAdministrationMessage,
            StoreNetworkMode.BlockedModuleMessage("usuarios"));

        var manage = Assert.Throws<InvalidOperationException>(
            ApplicationLoginService.EnsureLocalUserManagement);
        Assert.Equal(ApplicationLoginService.LocalUserAdministrationMessage, manage.Message);

        ApplicationLoginService.RemoteLoginOverride = (_, _) => RemoteDto(3, "ven", "vendedor", UserPermissions.ForRole("vendedor"));
        Assert.True(ApplicationLoginService.TryLogin("ven", "x").Ok);
        var token = StoreNetworkClient.SessionToken;
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(key,'') || '=' || IFNULL(value,'') FROM app_settings;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = reader.GetString(0);
            if (!string.IsNullOrEmpty(token))
                Assert.DoesNotContain(token, row);
            Assert.DoesNotContain("ExpiresAt", row, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Client_CannotManageLocalUsers_StandaloneAndServerCan()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        var adminId = UsersService.Save(null, "adm", "Admin", "admin", true, "senha-adm");
        UsersService.Save(adminId, "adm", "Admin Loja", "admin", true, null);
        Assert.Equal("Admin Loja", UsersService.Get(adminId)!.Nome);
        Assert.Equal(CredentialValidationStatus.Success,
            AuthService.ValidateCredentials("adm", "senha-adm").Status);
        Assert.False(StoreNetworkMode.IsModuleBlockedOnClient("usuarios"));

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleServer);
        var gestorId = UsersService.Save(null, "ges", "Gestor", "gestor", true, "senha-ges");
        Assert.Equal("gestor", UsersService.Get(gestorId)!.Role);
        UsersService.Deactivate(gestorId);
        Assert.False(UsersService.Get(gestorId)!.Active);
        UsersService.Save(gestorId, "ges", "Gestor", "gestor", true, null);
        Assert.True(UsersService.Get(gestorId)!.Active);
        Assert.False(StoreNetworkMode.IsModuleBlockedOnClient("usuarios"));

        SetClient();
        Assert.True(StoreNetworkMode.IsModuleBlockedOnClient("usuarios"));
        var before = SetupService.CountUsers();
        var create = Assert.Throws<InvalidOperationException>(() =>
            UsersService.Save(null, "nb", "Notebook", "vendedor", true, "senha-nb"));
        Assert.Equal(ApplicationLoginService.LocalUserAdministrationMessage, create.Message);
        Assert.Equal(before, SetupService.CountUsers());

        var edit = Assert.Throws<InvalidOperationException>(() =>
            UsersService.Save(adminId, "adm", "Hack", "admin", true, null));
        Assert.Equal(ApplicationLoginService.LocalUserAdministrationMessage, edit.Message);
        Assert.Equal("Admin Loja", UsersService.Get(adminId)!.Nome);

        var deactivate = Assert.Throws<InvalidOperationException>(() => UsersService.Deactivate(adminId));
        Assert.Equal(ApplicationLoginService.LocalUserAdministrationMessage, deactivate.Message);
        Assert.True(UsersService.Get(adminId)!.Active);

        TestDataHelper.SetSessionRole("admin");
        var reset = Assert.Throws<InvalidOperationException>(() =>
            UsersService.ResetPasswordByAdmin(adminId, "nova-senha"));
        Assert.Equal(ApplicationLoginService.PasswordChangeUnavailableMessage, reset.Message);
        Assert.Equal(CredentialValidationStatus.Success,
            AuthService.ValidateCredentials("adm", "senha-adm").Status);

        ApplicationLoginService.RemoteLoginOverride = (_, _) =>
            RemoteDto(88, "ven", "vendedor", UserPermissions.ForRole("vendedor"));
        var remote = ApplicationLoginService.TryLogin("ven", "x");
        Assert.True(remote.Ok);
        Assert.Equal(88, AppSession.CurrentUser!.Id);
        Assert.Equal("vendedor", AppSession.CurrentUser.Role);
        Assert.NotEqual(adminId, AppSession.CurrentUser.Id);
    }

    [Fact]
    public void Sources_PinApiVersionAuditAndNoStartupProbe()
    {
        var app = ReadSource("App.xaml.cs");
        Assert.DoesNotContain("StoreNetworkClient.Ping", app);
        Assert.DoesNotContain("GetPairingStatus", app);
        Assert.Contains("ApplicationLoginService.ShouldRunInitialSetup", app);

        var login = ReadSource(Path.Combine("Views", "LoginWindow.xaml.cs"));
        Assert.DoesNotContain("AuthService.TryLogin", login);
        Assert.Contains("ApplicationLoginService.TryLogin", login);

        var client = ReadSource(Path.Combine("Services", "StoreNetworkClient.cs"));
        Assert.Contains("X-Store-Pin", client);
        Assert.Contains("HandleAuthenticatedRequestUnauthorized", client);
        Assert.DoesNotContain("HandleAuthenticatedRequestUnauthorized",
            client[client.IndexOf("public static StoreNetworkSessionDto LoginRemote", StringComparison.Ordinal)
                  ..client.IndexOf("public static void LogoutRemote", StringComparison.Ordinal)]);

        var host = ReadSource(Path.Combine("Services", "StoreNetworkHost.cs"));
        Assert.Contains("apiVersion = 2", host);

        var audit = ReadSource(Path.Combine("Services", "AuditService.cs"));
        Assert.DoesNotContain("StoreNetworkClient.RemoteUser", audit);
        Assert.DoesNotContain("ApplicationLoginService", audit);

        var payable = ReadSource(Path.Combine("Services", "PayableService.cs"));
        Assert.DoesNotContain("remoteSession.Permissions", payable);
        Assert.DoesNotContain("CurrentRemoteSession.Permissions", payable);
    }

    [Fact]
    public void Client_LoginRemote_DoesNotChangeHostAppSession_WhenCallingClientApiDirectly()
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
            StoreNetworkClient.Pair(StoreNetworkPairingService.GenerateCode().Code);
            var dto = StoreNetworkClient.LoginRemote("ven", "senha-ven");
            Assert.Equal("ven", dto.User!.Login);
            Assert.Equal(hostLogin, AppSession.CurrentUser!.Login);
        }
        finally
        {
            StoreNetworkClient.ClearSessionState();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    private static void AssertOk(string login, string password, string role)
    {
        var r = ApplicationLoginService.TryLogin(login, password);
        Assert.True(r.Ok, r.Error);
        Assert.False(r.UsedRemoteLogin);
        Assert.Equal(role, r.User!.Role);
        Assert.Equal(password, r.TypedPassword);
    }

    private static int CreateUser(string login, string role, string password, bool active = true) =>
        SeedHostUser(() => UsersService.Save(null, login, login, role, active, password));

    private static T SeedHostUser<T>(Func<T> fn)
    {
        var role = StoreNetworkMode.GetRole();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        try
        {
            return fn();
        }
        finally
        {
            StoreNetworkMode.SetRole(role);
        }
    }

    private static void SeedHostUser(Action action) =>
        SeedHostUser(() =>
        {
            action();
            return true;
        });

    private static void SetClient()
    {
        StoreNetworkMode.SaveClient("192.0.2.1", "1234", StoreNetworkMode.DefaultPort);
        StoreNetworkMode.SaveServerFingerprint(new string('A', 64));
    }

    private static StoreNetworkSessionDto RemoteDto(int id, string login, string role, UserPermissions permissions) =>
        new()
        {
            Ok = true,
            Token = "t",
            User = new StoreNetworkRemoteUserDto
            {
                Id = id,
                Login = login,
                Name = login,
                Role = role,
                Permissions = permissions,
            },
        };

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

    private static string ReadSource(string relative)
    {
        var probe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "SGDB.App", relative));
        Assert.True(File.Exists(probe), probe);
        return File.ReadAllText(probe);
    }

    private sealed class CertDirScope : IDisposable
    {
        public string Directory { get; }

        public CertDirScope()
        {
            Directory = Path.Combine(Path.GetTempPath(), "SGDB.Tests.login", Guid.NewGuid().ToString("N"));
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
            ApplicationLoginService.ResetForTests();
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch { /* ignore */ }
        }
    }
}
