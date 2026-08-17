using SGDB.Models;

namespace SGDB.Services;

/// <summary>Primeiro boot (empresa + admin) e recuperação de senha offline.</summary>
public static class SetupService
{
    public const string KeySetupCompleted = "setup_completed";
    public const string KeyRecoveryHash = "recovery_code_hash";
    public const string FactoryLogin = "admin";
    public const string FactoryPassword = "admin";

    public static int CountUsers()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static bool NeedsInitialSetup() => CountUsers() == 0;

    public static bool HasRecoveryCode() =>
        !string.IsNullOrWhiteSpace(AppSettingsService.GetSetting(KeyRecoveryHash));

    /// <summary>Credenciais de fábrica ainda em uso (instalação antiga com seed admin/admin).</summary>
    public static bool IsFactoryDefaultPassword(string login, string password) =>
        string.Equals((login ?? "").Trim(), FactoryLogin, StringComparison.OrdinalIgnoreCase)
        && password == FactoryPassword;

    /// <summary>
    /// Grava empresa, cria admin e um código de recuperação (mostrar uma vez).
    /// </summary>
    public static string CompleteInitialSetup(
        CompanyProfile company,
        string adminLogin,
        string adminNome,
        string password)
    {
        ApplicationLoginService.EnsureLocalUserAdministration();
        if (CountUsers() > 0)
            throw new InvalidOperationException("A configuração inicial já foi concluída.");

        adminLogin = (adminLogin ?? "").Trim().ToLowerInvariant();
        adminNome = (adminNome ?? "").Trim();
        if (string.IsNullOrWhiteSpace(company.NomeFantasia) && string.IsNullOrWhiteSpace(company.RazaoSocial))
            throw new InvalidOperationException("Informe o nome fantasia ou a razão social.");
        if (string.IsNullOrWhiteSpace(adminLogin))
            throw new InvalidOperationException("Informe o login do administrador.");
        if (string.IsNullOrWhiteSpace(adminNome))
            throw new InvalidOperationException("Informe o nome do administrador.");
        if (string.IsNullOrEmpty(password) || password.Length < 4)
            throw new InvalidOperationException("A senha deve ter pelo menos 4 caracteres.");
        if (IsFactoryDefaultPassword(adminLogin, password))
            throw new InvalidOperationException("Escolha uma senha diferente da senha padrão de fábrica.");

        if (string.IsNullOrWhiteSpace(company.NomeFantasia))
            company.NomeFantasia = company.RazaoSocial.Trim();

        AppSettingsService.SaveCompanyProfile(company);
        UsersService.Save(
            null,
            adminLogin,
            adminNome,
            "admin",
            active: true,
            password,
            UserPermissions.ForRole("admin"));

        var recovery = GenerateRecoveryCode();
        AppSettingsService.SetSetting(KeyRecoveryHash, AuthService.HashPasswordCompatible(recovery));
        AppSettingsService.SetSetting(KeySetupCompleted, "1");
        return recovery;
    }

    public static bool TryResetPasswordWithRecovery(string login, string recoveryCode, string newPassword, out string error)
    {
        error = "";
        if (StoreNetworkMode.IsClient)
        {
            error = ApplicationLoginService.PasswordChangeUnavailableMessage;
            return false;
        }

        login = (login ?? "").Trim().ToLowerInvariant();
        recoveryCode = (recoveryCode ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(recoveryCode))
        {
            error = "Informe o login e o código de recuperação.";
            return false;
        }
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 4)
        {
            error = "A nova senha deve ter pelo menos 4 caracteres.";
            return false;
        }

        var hash = AppSettingsService.GetSetting(KeyRecoveryHash);
        if (string.IsNullOrWhiteSpace(hash))
        {
            error = "Nenhum código de recuperação cadastrado. Use o suporte ou reconfigure o sistema.";
            return false;
        }

        if (!AuthService.VerifyPasswordCompatible(recoveryCode, hash))
        {
            error = "Código de recuperação inválido.";
            return false;
        }

        var users = UsersService.List(login, "todos");
        var user = users.FirstOrDefault(u =>
            string.Equals(u.Login, login, StringComparison.OrdinalIgnoreCase));
        if (user is null || !user.Active)
        {
            error = "Usuário não encontrado ou inativo.";
            return false;
        }

        UsersService.Save(user.Id, user.Login, user.Nome, user.Role, true, newPassword, user.Permissions);
        AuditService.Log("recuperacao_senha", "usuario", user.Id.ToString(), user.Login);
        return true;
    }

    /// <summary>Gera novo código (ex.: admin regenerando). Retorna o código em texto claro.</summary>
    public static string RegenerateRecoveryCode()
    {
        var code = GenerateRecoveryCode();
        AppSettingsService.SetSetting(KeyRecoveryHash, AuthService.HashPasswordCompatible(code));
        return code;
    }

    private static string GenerateRecoveryCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        var sb = new System.Text.StringBuilder(8);
        foreach (var b in bytes)
            sb.Append(alphabet[b % alphabet.Length]);
        return sb.ToString();
    }
}
