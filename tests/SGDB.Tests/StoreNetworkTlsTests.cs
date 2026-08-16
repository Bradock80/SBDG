using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>ETAPA 68C2 — TLS, certificado DPAPI e pinning SHA-256 da Rede Loja.</summary>
[Collection(TempDatabaseCollection.Name)]
public class StoreNetworkTlsTests
{
    [Fact]
    public void Certificate_IsGenerated_WhenMissing_AndReloadsWithStableFingerprint()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();

        Assert.False(StoreNetworkCertificateService.FileExists);
        using var first = StoreNetworkCertificateService.LoadOrCreate("Loja Teste");
        Assert.True(first.HasPrivateKey);
        var fp = StoreNetworkCertificateService.ComputeFingerprint(first);
        Assert.Equal(64, fp.Length);

        using var second = StoreNetworkCertificateService.LoadOrCreate("Loja Teste");
        Assert.Equal(fp, StoreNetworkCertificateService.ComputeFingerprint(second));
        Assert.Equal(StoreNetworkCertificateStatus.Ok, StoreNetworkCertificateService.GetFileStatus());
    }

    [Fact]
    public void Certificate_PrivateKey_IsNotPlaintextOnDisk()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var cert = StoreNetworkCertificateService.LoadOrCreate("Loja");
        var path = StoreNetworkCertificateService.CertificateFilePath;
        var raw = File.ReadAllBytes(path);
        Assert.True(raw.Length > 32);
        var parsed = false;
        try
        {
            using var bogus = new X509Certificate2(raw);
            parsed = bogus.Handle != IntPtr.Zero && bogus.HasPrivateKey;
        }
        catch
        {
            parsed = false;
        }
        Assert.False(parsed, "O arquivo DPAPI não deve abrir como PFX em texto claro.");
        using var loaded = StoreNetworkCertificateService.LoadFromFile(path);
        Assert.True(loaded.HasPrivateKey);
        Assert.Equal(
            StoreNetworkCertificateService.ComputeFingerprint(cert),
            StoreNetworkCertificateService.ComputeFingerprint(loaded));
    }

    [Fact]
    public void Fingerprint_Normalize_AndFixedTimeMatch()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var cert = StoreNetworkCertificateService.LoadOrCreate("Loja");
        var fp = StoreNetworkCertificateService.ComputeFingerprint(cert);
        var spaced = StoreNetworkCertificateService.NormalizeFingerprint(
            string.Join(" ", Enumerable.Range(0, 16).Select(i => fp.Substring(i * 4, 4))));
        Assert.Equal(fp, spaced);
        Assert.True(StoreNetworkCertificateService.MatchesFingerprint(cert, fp));
        Assert.False(StoreNetworkCertificateService.MatchesFingerprint(cert, new string('B', 64)));
        Assert.False(StoreNetworkCertificateService.MatchesFingerprint(null, fp));
    }

    [Fact]
    public void CorruptedCertificate_BlocksStartup_DoesNotRegenerate()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using (StoreNetworkCertificateService.LoadOrCreate("Loja")) { }
        var path = StoreNetworkCertificateService.CertificateFilePath;
        var original = File.ReadAllBytes(path);
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03, 0xFF]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => StoreNetworkCertificateService.LoadOrCreate("Loja"));
        Assert.Contains("NÃO foi gerado", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StoreNetworkCertificateStatus.Unreadable, StoreNetworkCertificateService.GetFileStatus());
        Assert.NotEqual(original.Length, File.ReadAllBytes(path).Length);
    }

    [Fact]
    public void ExpiredCertificate_BlocksStartup_DoesNotRegenerate()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var expired = StoreNetworkCertificateService.CreateSelfSigned(
            "Loja",
            DateTimeOffset.UtcNow.AddYears(-3),
            DateTimeOffset.UtcNow.AddDays(-1),
            []);
        StoreNetworkCertificateService.SaveCertificate(expired);
        var before = File.ReadAllBytes(StoreNetworkCertificateService.CertificateFilePath);

        var ex = Assert.Throws<InvalidOperationException>(
            () => StoreNetworkCertificateService.LoadOrCreate("Loja"));
        Assert.Contains("expirou", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllBytes(StoreNetworkCertificateService.CertificateFilePath));
        Assert.Equal(StoreNetworkCertificateStatus.Expired, StoreNetworkCertificateService.GetFileStatus());
    }

    [Fact]
    public void Client_ForcesHttp11_Https_ConnectTimeout_NoDangerousAccept()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), StoreNetworkClient.ConnectTimeout);
        Assert.Equal(HttpVersion.Version11, StoreNetworkClient.RequiredHttpVersion);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, StoreNetworkClient.RequiredVersionPolicy);

        var probe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "SGDB.App", "Services", "StoreNetworkClient.cs"));
        Assert.True(File.Exists(probe), probe);
        var text = File.ReadAllText(probe);
        Assert.DoesNotContain("DangerousAcceptAny", text);
        Assert.Contains("MatchesFingerprint", text);
        Assert.Contains("https://", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClientBaseUrl_IsHttps()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SaveClient("127.0.0.1", "1234", 5055);
        try
        {
            Assert.StartsWith("https://", StoreNetworkMode.ClientBaseUrl, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("127.0.0.1:5055", StoreNetworkMode.ClientBaseUrl);
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void WithoutFingerprint_DoesNotConnect()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SaveClient("127.0.0.1", "1234", 5055);
        StoreNetworkMode.ClearServerFingerprint();
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                StoreNetworkClient.ListProducts(null, "ativos", null, null, null, "none"));
            Assert.Contains("ainda não confia", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void TlsHost_StatusAndProducts_WithCorrectPinAndFingerprint()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        ConfigureClient(host, "2468", host.CertificateFingerprint!);

        try
        {
            var st = StoreNetworkClient.Login("2468");
            Assert.True(st.Ok);
            var ping = StoreNetworkClient.Ping();
            Assert.True(ping.Ok);

            for (var i = 0; i < 40; i++)
                TestDataHelper.SeedSimpleProduct(10, 5, 2, $"TLS{i:000}", $"Produto TLS {i}");

            var list = StoreNetworkClient.ListProducts(null, "ativos", null, null, null, "none");
            Assert.True(list.Count >= 40, $"esperava >= 40 produtos, veio {list.Count}");
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void TlsHost_WrongPin_StillTls_ButUnauthorized()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        ConfigureClient(host, "2468", host.CertificateFingerprint!);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => StoreNetworkClient.Login("9999"));
            Assert.Contains("PIN", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void WrongFingerprint_Rejects_AndDoesNotOverwriteSavedFingerprint()
    {
        using var db = TempDatabase.Create();
        using var otherDir = new CertDirScope();
        using var other = StoreNetworkCertificateService.LoadOrCreate("Outra");
        var otherFp = StoreNetworkCertificateService.ComputeFingerprint(other);
        otherDir.ReleaseOverride();

        using var hostDir = new CertDirScope();
        using var host = StartHost("2468");
        Assert.NotEqual(otherFp, host.CertificateFingerprint);
        ConfigureClient(host, "2468", otherFp);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => StoreNetworkClient.Login("2468"));
            Assert.Contains("certificado da loja mudou", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(otherFp, StoreNetworkMode.GetServerFingerprint());
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void FakeServerCertificate_IsRejected()
    {
        using var db = TempDatabase.Create();
        using var scope = new CertDirScope();
        using var host = StartHost("2468");
        var fakeFp = new string('C', 64);
        ConfigureClient(host, "2468", fakeFp);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => StoreNetworkClient.Ping());
            Assert.Contains("certificado da loja mudou", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(fakeFp, StoreNetworkMode.GetServerFingerprint());
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
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

    private sealed class CertDirScope : IDisposable
    {
        public string Directory { get; }

        public CertDirScope()
        {
            Directory = Path.Combine(Path.GetTempPath(), "SGDB.Tests.tls", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            StoreNetworkCertificateService.OverrideDirectory = Directory;
        }

        public void ReleaseOverride()
        {
            if (StoreNetworkCertificateService.OverrideDirectory == Directory)
                StoreNetworkCertificateService.OverrideDirectory = null;
        }

        public void Dispose()
        {
            ReleaseOverride();
            StoreNetworkHost.Current?.Dispose();
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch { /* ignore */ }
        }
    }
}
