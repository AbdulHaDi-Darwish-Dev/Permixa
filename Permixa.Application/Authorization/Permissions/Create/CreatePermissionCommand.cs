namespace Permixa.Application.Authorization.Permissions.Create;

public sealed record CreatePermissionCommand(Guid ActorUserId, string Name, string? Description);
