using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SGDB.Services;

/// <summary>
/// Lembra usuário/senha neste PC (DPAPI — só funciona para o Windows do usuário atual).
/// </summary>
public static class LoginRememberService
{
    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SGDB",
            "login_remember.bin");

    public static (string Login, string Password)? TryLoad()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            var protectedBytes = File.ReadAllBytes(FilePath);
            var plain = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plain);
            var data = JsonSerializer.Deserialize<RememberPayload>(json);
            if (data is null || string.IsNullOrWhiteSpace(data.Login))
                return null;
            return (data.Login.Trim(), data.Password ?? "");
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string login, string password)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(new RememberPayload
        {
            Login = (login ?? "").Trim(),
            Password = password ?? "",
        });
        var plain = Encoding.UTF8.GetBytes(json);
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

    private sealed class RememberPayload
    {
        public string Login { get; set; } = "";
        public string Password { get; set; } = "";
    }
}
