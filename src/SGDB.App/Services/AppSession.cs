using SGDB.Models;

namespace SGDB.Services;

/// <summary>Usuário logado na sessão atual do aplicativo.</summary>
public static class AppSession
{
    public static User? CurrentUser { get; private set; }

    public static UserPermissions Permissions { get; private set; } =
        UserPermissions.ForRole("vendedor");

    public static string UserDisplay =>
        CurrentUser is null ? "Sistema" : $"{CurrentUser.Nome} ({CurrentUser.Login})";

    public static string UserLogin => CurrentUser?.Login ?? "sistema";

    public static bool IsAdmin =>
        string.Equals(CurrentUser?.Role, "admin", StringComparison.OrdinalIgnoreCase);

    public static void SetUser(User? user)
    {
        CurrentUser = user;
        if (user is null)
        {
            Permissions = UserPermissions.ForRole("vendedor");
            return;
        }

        Permissions = ResolvePermissions(user);
    }

    public static void Clear()
    {
        CurrentUser = null;
        Permissions = UserPermissions.ForRole("vendedor");
    }

    /// <summary>Recarrega permissions_json do banco (após salvar usuário na mesma sessão).</summary>
    public static void RefreshPermissions()
    {
        if (CurrentUser is null) return;
        Permissions = ResolvePermissions(CurrentUser, reloadFromLocalDatabase: true);
    }

    private static UserPermissions ResolvePermissions(User user, bool reloadFromLocalDatabase = false)
    {
        if (!reloadFromLocalDatabase && user.Permissions is not null)
            return user.Permissions;

        if (IsClientProcess())
            return user.Permissions ?? UserPermissions.ForRole(user.Role);

        return UsersService.GetPermissions(user.Id);
    }

    private static bool IsClientProcess()
    {
        try
        {
            return StoreNetworkMode.IsClient;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
