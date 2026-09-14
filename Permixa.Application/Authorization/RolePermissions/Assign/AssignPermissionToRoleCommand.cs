namespace Permixa.Application.Authorization.RolePermissions.Assign;

public sealed record AssignPermissionToRoleCommand(
    Guid ActorUserId,
    Guid RoleId,
    Guid PermissionId);
