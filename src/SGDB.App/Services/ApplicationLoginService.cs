using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// ETAPA 68C3-B3 — autenticação do processo atual.
/// Standalone/servidor: AuthService local.
/// Cliente: LoginRemote do PC da loja, sem fallback para o SQLite do notebook.
/// </summary>
public static class ApplicationLoginService
{
    public const string MissingFingerprintMessage =
        "Este computador ainda não confia no PC da loja.\n" +
        "Configure a Rede Loja e confirme o fingerprint do servidor.";

    public const string FingerprintMismatchMessage =
        "A identidade do PC da loja não pôde ser confirmada.\n" +
        "Confira o fingerprint da Rede Loja antes de continuar.";

    public const string DeviceNotPairedMessage =
        "Este computador ainda não está autorizado pela loja.\n" +
        "Faça o pareamento em Sistema > Rede Loja.";

    public const string DeviceRevokedMessage =
        StoreNetworkClient.DeviceRevokedMessage;

    public const string ServerOfflineMessage =
        "Não foi possível conectar ao PC da loja.\n" +
        "Verifique se o computador da loja está ligado e se a Rede Loja está disponível.";

    public const string InvalidCredentialsMessage =
        StoreNetworkSessionService.InvalidCredentialsMessage;

    public const string InactiveAccountMessage =
        StoreNetworkSessionService.InactiveAccountMessage;

    public const string SessionExpiredMessage =
        "Esta sessão não é mais válida. Entre novamente.";

    public const string PasswordChangeUnavailableMessage =
        "A alteração de senha pela Rede Loja ainda não está disponível neste computador.";

    public const string RegisterOnServerMessage =
        "Cadastre usuários no PC da loja.";

    public const string LocalUserAdministrationMessage =
        "Os usuários da Rede Loja são administrados no PC da loja.";

    public static event Action<StoreNetworkSessionExpiredException>? RemoteSessionInvalidated;

    internal static int SessionExpiredNotifications { get; private set; }
    internal static bool SessionInvalidationInProgress { get; private set; }

    internal static Func<string, string, StoreNetworkSessionDto>? RemoteLoginOverride { get; set; }

    public static bool IsRemoteLogin => StoreNetworkMode.IsClient;

    public static bool ShouldRunInitialSetup() =>
        !StoreNetworkMode.IsClient && SetupService.NeedsInitialSetup();

    public static bool ShouldForceLocalPasswordChange(User user, string? typedPassword) =>
        !StoreNetworkMode.IsClient
        && typedPassword is not null
        && SetupService.IsFactoryDefaultPassword(user.Login, typedPassword);

    public static bool CanRememberPassword => !StoreNetworkMode.IsClient;

    public static ApplicationLoginResult TryLogin(string? login, string? password)
    {
        password ??= "";
        if (!StoreNetworkMode.IsClient)
            return TryLocalLogin(login, password);

        return TryRemoteLogin(login, password);
    }

    public static void Logout()
    {
        try
        {
            if (StoreNetworkMode.IsClient)
                StoreNetworkClient.LogoutRemote();
            else
                StoreNetworkClient.ClearSessionState();
        }
        finally
        {
            AppSession.Clear();
            SessionInvalidationInProgress = false;
        }
    }

    /// <summary>
    /// Fecha a sessão local sem esperar HTTP — usado ao encerrar o processo.
    /// </summary>
    public static void AbandonLocalSession()
    {
        StoreNetworkClient.ClearSessionState();
        AppSession.Clear();
        SessionInvalidationInProgress = false;
    }

    public static void EnsureLocalUserAdministration()
    {
        if (StoreNetworkMode.IsClient)
            throw new InvalidOperationException(RegisterOnServerMessage);
    }

    public static void EnsureLocalUserManagement()
    {
        if (StoreNetworkMode.IsClient)
            throw new InvalidOperationException(LocalUserAdministrationMessage);
    }

    public static void EnsureLocalPasswordChange()
    {
        if (StoreNetworkMode.IsClient)
            throw new InvalidOperationException(PasswordChangeUnavailableMessage);
    }

    public static User MaterializeRemoteUser(StoreNetworkRemoteUserDto? dto)
    {
        if (dto is null || dto.Id <= 0 || string.IsNullOrWhiteSpace(dto.Login))
            throw new InvalidOperationException("Resposta inválida do servidor.");

        var role = string.IsNullOrWhiteSpace(dto.Role) ? "vendedor" : dto.Role.Trim();
        var permissions = dto.Permissions ?? UserPermissions.ForRole(role);
        return new User
        {
            Id = dto.Id,
            Login = dto.Login.Trim(),
            Nome = string.IsNullOrWhiteSpace(dto.Name) ? dto.Login.Trim() : dto.Name.Trim(),
            Role = role,
            Permissions = permissions,
        };
    }

    internal static void HandleAuthenticatedRequestUnauthorized(string? serverError)
    {
        var err = serverError ?? "";
        if (LooksLikeInvalidLogin(err))
            return;

        var kind = LooksLikeDeviceRevoked(err)
            ? StoreNetworkSessionExpiredKind.DeviceRevoked
            : StoreNetworkSessionExpiredKind.InvalidSession;
        var message = kind == StoreNetworkSessionExpiredKind.DeviceRevoked
            ? DeviceRevokedMessage
            : SessionExpiredMessage;

        StoreNetworkClient.ClearSessionState();
        if (StoreNetworkMode.IsClient)
            AppSession.Clear();

        var ex = new StoreNetworkSessionExpiredException(kind, message);
        if (!SessionInvalidationInProgress)
        {
            SessionInvalidationInProgress = true;
            SessionExpiredNotifications++;
            try
            {
                RemoteSessionInvalidated?.Invoke(ex);
            }
            catch
            {
                /* a UI trata; não reentra */
            }
        }

        throw ex;
    }

    internal static void ResetForTests()
    {
        RemoteLoginOverride = null;
        SessionInvalidationInProgress = false;
        SessionExpiredNotifications = 0;
        RemoteSessionInvalidated = null;
    }

    internal static string MapClientException(Exception ex)
    {
        var msg = ex.Message ?? "";
        if (ex is StoreNetworkSessionExpiredException expired)
            return expired.Message;
        if (StoreNetworkClient.IsCertificateMismatch(ex)
            || msg.Contains(StoreNetworkClient.FingerprintMismatchMessage, StringComparison.OrdinalIgnoreCase)
            || msg.Contains("certificado da loja mudou", StringComparison.OrdinalIgnoreCase))
            return FingerprintMismatchMessage;
        if (msg.Contains(StoreNetworkClient.MissingFingerprintMessage, StringComparison.OrdinalIgnoreCase)
            || msg.Contains("ainda não confia", StringComparison.OrdinalIgnoreCase))
            return MissingFingerprintMessage;
        if (LooksLikeDeviceRevoked(msg))
            return DeviceRevokedMessage;
        if (msg.Contains(StoreNetworkPairingService.InvalidDeviceIdStoredMessage, StringComparison.OrdinalIgnoreCase)
            || msg.Contains("identidade deste computador está inválida", StringComparison.OrdinalIgnoreCase))
            return StoreNetworkPairingService.InvalidDeviceIdStoredMessage;
        if (msg.Contains(StoreNetworkSessionService.DeviceNotAuthorizedMessage, StringComparison.OrdinalIgnoreCase)
            || msg.Contains("não está autorizado", StringComparison.OrdinalIgnoreCase))
            return DeviceNotPairedMessage;
        if (LooksLikeInvalidLogin(msg))
            return InvalidCredentialsMessage;
        if (msg.Contains(StoreNetworkSessionService.InactiveAccountMessage, StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Conta indisponível", StringComparison.OrdinalIgnoreCase))
            return InactiveAccountMessage;
        if (msg.Contains(StoreNetworkClient.TlsRequiredMessage, StringComparison.OrdinalIgnoreCase))
            return StoreNetworkClient.TlsRequiredMessage;
        if (msg.Contains("Não conectou", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("actively refused", StringComparison.OrdinalIgnoreCase))
            return ServerOfflineMessage;
        return string.IsNullOrWhiteSpace(msg) ? ServerOfflineMessage : msg;
    }

    private static ApplicationLoginResult TryLocalLogin(string? login, string? password)
    {
        try
        {
            var user = AuthService.TryLogin(login ?? "", password ?? "");
            if (user is null)
            {
                return ApplicationLoginResult.Failed(InvalidCredentialsMessage);
            }

            return ApplicationLoginResult.Succeeded(user, password, usedRemoteLogin: false);
        }
        catch (AuthPendingException ex)
        {
            return ApplicationLoginResult.Failed(ex.Message);
        }
    }

    private static ApplicationLoginResult TryRemoteLogin(string? login, string? password)
    {
        try
        {
            StoreNetworkSessionDto dto;
            if (RemoteLoginOverride is not null)
            {
                dto = RemoteLoginOverride(login ?? "", password ?? "");
            }
            else
            {
                if (!StoreNetworkMode.HasServerFingerprint())
                    return ApplicationLoginResult.Failed(MissingFingerprintMessage, canOpenStoreNetworkSettings: true);

                dto = StoreNetworkClient.LoginRemote(login ?? "", password ?? "");
            }

            var user = MaterializeRemoteUser(dto.User ?? StoreNetworkClient.RemoteUser);
            AppSession.SetUser(user);
            SessionInvalidationInProgress = false;
            return ApplicationLoginResult.Succeeded(user, typedPassword: null, usedRemoteLogin: true);
        }
        catch (StoreNetworkSessionExpiredException ex)
        {
            return ApplicationLoginResult.Failed(ex.Message);
        }
        catch (Exception ex)
        {
            var mapped = MapClientException(ex);
            var openSettings = mapped == MissingFingerprintMessage
                               || mapped == FingerprintMismatchMessage
                               || mapped == DeviceNotPairedMessage;
            return ApplicationLoginResult.Failed(mapped, canOpenStoreNetworkSettings: openSettings);
        }
    }

    private static bool LooksLikeInvalidLogin(string msg) =>
        msg.Contains(StoreNetworkSessionService.InvalidCredentialsMessage, StringComparison.OrdinalIgnoreCase)
        || msg.Contains("Usuário ou senha inválidos", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeDeviceRevoked(string msg) =>
        msg.Contains(StoreNetworkClient.DeviceRevokedMessage, StringComparison.OrdinalIgnoreCase)
        || msg.Contains("não está mais autorizado", StringComparison.OrdinalIgnoreCase);
}

public sealed class ApplicationLoginResult
{
    public bool Ok { get; init; }
    public User? User { get; init; }
    public string? TypedPassword { get; init; }
    public string? Error { get; init; }
    public bool UsedRemoteLogin { get; init; }
    public bool CanOpenStoreNetworkSettings { get; init; }

    public static ApplicationLoginResult Succeeded(User user, string? typedPassword, bool usedRemoteLogin) =>
        new()
        {
            Ok = true,
            User = user,
            TypedPassword = typedPassword,
            UsedRemoteLogin = usedRemoteLogin,
        };

    public static ApplicationLoginResult Failed(string error, bool canOpenStoreNetworkSettings = false) =>
        new()
        {
            Error = error,
            CanOpenStoreNetworkSettings = canOpenStoreNetworkSettings,
        };
}

public enum StoreNetworkSessionExpiredKind
{
    InvalidSession,
    DeviceRevoked,
}

public sealed class StoreNetworkSessionExpiredException : InvalidOperationException
{
    public StoreNetworkSessionExpiredKind Kind { get; }

    public StoreNetworkSessionExpiredException(StoreNetworkSessionExpiredKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }
}
