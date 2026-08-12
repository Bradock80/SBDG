namespace SGDB.Models;

public sealed class User
{
    public int Id { get; init; }
    public required string Login { get; init; }
    public required string Nome { get; init; }
    public required string Role { get; init; }
    public UserPermissions? Permissions { get; init; }
}
