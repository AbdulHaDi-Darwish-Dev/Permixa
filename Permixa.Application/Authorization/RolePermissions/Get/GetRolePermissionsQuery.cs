namespace Permixa.Application.Authorization.RolePermissions.Get;

public sealed record GetRolePermissionsQuery(Guid ActorUserId, Guid RoleId);
