using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SGDB.Services;

/// <summary>
/// Certificado TLS da Rede Loja: PFX em arquivo DPAPI (fora do deposito.db).
/// ETAPA 68C2 — transporte; pairing/login remoto ficam para 68C3.
/// </summary>
public static class StoreNetworkCertificateService
{
    public const string FileName = "store_network_certificate.dat";
    public const int FingerprintHexLength = 64;

    private static readonly byte[] DpapiEntropy =
        Encoding.UTF8.GetBytes("SGDB.StoreNetwork.Certificate.v1");

    /// <summary>Pasta alternativa (testes). Null = %LOCALAPPDATA%\SGDB.</summary>
    public static string? OverrideDirectory { get; set; }

    public static string DirectoryPath =>
        OverrideDirectory
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SGDB");

    public static string CertificateFilePath => Path.Combine(DirectoryPath, FileName);

    public static bool FileExists => File.Exists(CertificateFilePath);

    public static string ComputeFingerprint(X509Certificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var hash = SHA256.HashData(certificate.GetRawCertData());
        return Convert.ToHexString(hash);
    }

    /// <summary>Remove espaços/dois-pontos e normaliza para hex maiúsculo de 64 chars.</summary>
    public static string NormalizeFingerprint(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var sb = new StringBuilder(FingerprintHexLength);
        foreach (var c in raw)
        {
            if (char.IsWhiteSpace(c) || c is ':' or '-')
                continue;
            if (char.IsAsciiHexDigit(c))
                sb.Append(char.ToUpperInvariant(c));
            else
                throw new InvalidOperationException(
                    "Fingerprint inválido. Cole o SHA-256 hexadecimal exibido no PC da loja.");
        }

        var hex = sb.ToString();
        if (hex.Length != FingerprintHexLength)
            throw new InvalidOperationException(
                "Fingerprint inválido. Deve ter 64 caracteres hexadecimais (SHA-256).");
        return hex;
    }

    public static bool TryNormalizeFingerprint(string? raw, out string normalized)
    {
        try
        {
            normalized = NormalizeFingerprint(raw);
            return normalized.Length == FingerprintHexLength;
        }
        catch
        {
            normalized = "";
            return false;
        }
    }

    public static bool MatchesFingerprint(X509Certificate? certificate, string expectedNormalizedHex)
    {
        if (certificate is null)
            return false;
        if (string.IsNullOrWhiteSpace(expectedNormalizedHex)
            || expectedNormalizedHex.Length != FingerprintHexLength)
            return false;

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedNormalizedHex);
        }
        catch
        {
            return false;
        }

        var actual = SHA256.HashData(certificate.GetRawCertData());
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static StoreNetworkCertificateStatus GetFileStatus()
    {
        if (!FileExists)
            return StoreNetworkCertificateStatus.Missing;
        try
        {
            using var cert = LoadFromFile(CertificateFilePath);
            if (cert.NotAfter <= DateTime.Now)
                return StoreNetworkCertificateStatus.Expired;
            if (!cert.HasPrivateKey)
                return StoreNetworkCertificateStatus.Unreadable;
            return StoreNetworkCertificateStatus.Ok;
        }
        catch
        {
            return StoreNetworkCertificateStatus.Unreadable;
        }
    }

    /// <summary>Lê fingerprint sem gerar certificado. Null se ausente/ilegível.</summary>
    public static string? TryReadFingerprint()
    {
        if (!FileExists)
            return null;
        try
        {
            using var cert = LoadFromFile(CertificateFilePath);
            return ComputeFingerprint(cert);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Carrega o PFX DPAPI. Se o arquivo não existe, gera. Se existe e não abre
    /// (corrompido / outro usuário Windows), NÃO gera outro — o fingerprint mudaria.
    /// </summary>
    public static X509Certificate2 LoadOrCreate(string? commonName)
    {
        if (!FileExists)
            return CreateAndSave(commonName);

        X509Certificate2 cert;
        try
        {
            cert = LoadFromFile(CertificateFilePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Não foi possível abrir o certificado da Rede Loja.\n" +
                "O arquivo existe, mas está corrompido ou pertence a outro usuário do Windows.\n" +
                "Um certificado novo NÃO foi gerado (o fingerprint mudaria).\n\n" +
                ex.Message);
        }

        if (!cert.HasPrivateKey)
        {
            cert.Dispose();
            throw new InvalidOperationException(
                "O certificado da Rede Loja não contém chave privada. " +
                "Um certificado novo NÃO foi gerado.");
        }

        if (cert.NotAfter <= DateTime.Now)
        {
            var until = cert.NotAfter;
            cert.Dispose();
            throw new InvalidOperationException(
                "O certificado da Rede Loja expirou em " + until.ToString("dd/MM/yyyy") + ".\n" +
                "Não foi renovado automaticamente porque o fingerprint mudaria.\n" +
                "Os notebooks precisarão configurar o fingerprint novamente após regenerar.");
        }

        return cert;
    }

    internal static X509Certificate2 CreateSelfSigned(
        string? commonName,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        IReadOnlyList<string>? extraIpv4)
    {
        using var rsa = RSA.Create(2048);
        var cn = SanitizeCn(commonName);
        var req = new CertificateRequest(
            $"CN={cn}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));
        req.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                critical: false));
        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));

        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        san.AddDnsName("localhost");
        foreach (var ip in extraIpv4 ?? [])
        {
            if (IPAddress.TryParse(ip, out var parsed)
                && parsed.AddressFamily == AddressFamily.InterNetwork)
                san.AddIpAddress(parsed);
        }

        req.CertificateExtensions.Add(san.Build());
        return req.CreateSelfSigned(notBefore, notAfter);
    }

    internal static void SaveCertificate(X509Certificate2 certificate, string? path = null)
    {
        var file = path ?? CertificateFilePath;
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var pfx = certificate.Export(X509ContentType.Pfx);
        var protectedBytes = ProtectedData.Protect(
            pfx,
            DpapiEntropy,
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(file, protectedBytes);
        CryptographicOperations.ZeroMemory(pfx.AsSpan());
    }

    internal static X509Certificate2 LoadFromFile(string path)
    {
        var protectedBytes = File.ReadAllBytes(path);
        byte[] pfx;
        try
        {
            pfx = ProtectedData.Unprotect(
                protectedBytes,
                DpapiEntropy,
                DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "DPAPI não conseguiu abrir o certificado (usuário Windows diferente ou arquivo adulterado).",
                ex);
        }

        try
        {
            return new X509Certificate2(
                pfx,
                (string?)null,
                X509KeyStorageFlags.Exportable
                | X509KeyStorageFlags.PersistKeySet
                | X509KeyStorageFlags.UserKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx.AsSpan());
        }
    }

    private static X509Certificate2 CreateAndSave(string? commonName)
    {
        Directory.CreateDirectory(DirectoryPath);
        IReadOnlyList<string> lanIps = [];
        try { lanIps = DeckCompanionHost.GetLanIPv4Addresses(); }
        catch { /* testes / máquina sem NIC */ }

        var cert = CreateSelfSigned(
            commonName,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(2),
            lanIps);
        SaveCertificate(cert);
        cert.Dispose();
        // Windows Schannel/SslStream exige a chave reimportada do PFX.
        return LoadFromFile(CertificateFilePath);
    }

    private static string SanitizeCn(string? name)
    {
        var s = (name ?? "SGDB").Trim();
        if (s.Length == 0)
            s = "SGDB";
        var sb = new StringBuilder(64);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.')
                sb.Append(c);
            if (sb.Length >= 64)
                break;
        }

        var clean = sb.ToString().Trim();
        return clean.Length == 0 ? "SGDB" : clean;
    }
}

public enum StoreNetworkCertificateStatus
{
    Missing,
    Ok,
    Expired,
    Unreadable,
}
