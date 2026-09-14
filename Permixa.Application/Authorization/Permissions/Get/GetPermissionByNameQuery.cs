namespace Permixa.Application.Authorization.Permissions.Get;

public sealed record GetPermissionByNameQuery(Guid ActorUserId, string PermissionName);
