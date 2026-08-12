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

        Permissions = user.Permissions
                      ?? UsersService.GetPermissions(user.Id);
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
        Permissions = UsersService.GetPermissions(CurrentUser.Id);
    }
}
