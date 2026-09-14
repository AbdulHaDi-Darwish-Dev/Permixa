namespace Permixa.Application.Authorization.RolePermissions.Remove;

public sealed record RemovePermissionFromRoleCommand(
    Guid ActorUserId,
    Guid RoleId,
    Guid PermissionId);