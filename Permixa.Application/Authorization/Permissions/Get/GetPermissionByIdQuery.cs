namespace Permixa.Application.Authorization.Permissions.Get;

public sealed record GetPermissionByIdQuery(Guid ActorUserId, Guid PermissionId);
