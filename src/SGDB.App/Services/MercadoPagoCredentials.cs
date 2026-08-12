using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SGDB.Services;

/// <summary>
/// Guarda o Access Token do Mercado Pago com DPAPI (só este Windows/usuário).
/// </summary>
public static class MercadoPagoCredentials
{
    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SGDB",
            "mercadopago_token.bin");

    public static bool HasToken()
    {
        var t = TryLoadAccessToken();
        return !string.IsNullOrWhiteSpace(t);
    }

    public static string? TryLoadAccessToken()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;
            var protectedBytes = File.ReadAllBytes(FilePath);
            var plain = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var token = Encoding.UTF8.GetString(plain).Trim();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveAccessToken(string? token)
    {
        token = (token ?? "").Trim();
        if (string.IsNullOrEmpty(token))
        {
            Clear();
            return;
        }

        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var plain = Encoding.UTF8.GetBytes(token);
        var protectedBytes = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, protectedBytes);
        AppSettingsService.SetSetting("mp_pix_enabled", "1");
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
        AppSettingsService.SetSetting("mp_pix_enabled", "0");
    }

    public static bool IsPixEnabled()
    {
        if (!HasToken())
            return false;
        var flag = AppSettingsService.GetSetting("mp_pix_enabled");
        return flag is null or "1" or "true" or "sim";
    }

    public static void SetPixEnabled(bool enabled) =>
        AppSettingsService.SetSetting("mp_pix_enabled", enabled ? "1" : "0");

    /// <summary>Máscara para exibir na tela (não revela o token).</summary>
    public static string MaskedTokenPreview()
    {
        var t = TryLoadAccessToken();
        if (string.IsNullOrEmpty(t))
            return "(não configurado)";
        if (t.Length <= 16)
            return t[..4] + "…" + t[^4..];
        return t[..12] + "…" + t[^8..];
    }
}
