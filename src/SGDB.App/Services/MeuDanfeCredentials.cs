using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SGDB.Services;

/// <summary>
/// Api-Key do Meu Danfe (busca de NF-e pela chave). Guardada com DPAPI neste Windows/usuário.
/// </summary>
public static class MeuDanfeCredentials
{
    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SGDB",
            "meudanfe_api_key.bin");

    public static bool HasApiKey()
    {
        var t = TryLoadApiKey();
        return !string.IsNullOrWhiteSpace(t);
    }

    public static string? TryLoadApiKey()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;
            var protectedBytes = File.ReadAllBytes(FilePath);
            var plain = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var key = Encoding.UTF8.GetString(plain).Trim();
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveApiKey(string? apiKey)
    {
        apiKey = (apiKey ?? "").Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            Clear();
            return;
        }

        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var plain = Encoding.UTF8.GetBytes(apiKey);
        var protectedBytes = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, protectedBytes);
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch
        {
            // ignore
        }
    }

    public static string MaskedApiKeyPreview()
    {
        var t = TryLoadApiKey();
        if (string.IsNullOrEmpty(t))
            return "(não configurado)";
        if (t.Length <= 8)
            return t[..2] + "…" + t[^2..];
        return t[..4] + "…" + t[^4..];
    }
}
