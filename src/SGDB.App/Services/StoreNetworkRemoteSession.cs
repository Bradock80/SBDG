using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Identidade remota da Rede Loja. Vive só em memória no host.
/// Não é AppSession.
/// </summary>
public sealed record StoreNetworkRemoteSession
{
    public required string Token { get; init; }
    public required int UserId { get; init; }
    public required string Login { get; init; }
    public required string UserName { get; init; }
    public required string Role { get; init; }
    public required UserPermissions Permissions { get; init; }
    public required string DeviceId { get; init; }
    public required string Origin { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
    public required string PasswordHashFingerprint { get; init; }
}
