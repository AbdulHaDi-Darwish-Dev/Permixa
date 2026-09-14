namespace Permixa.Application.Authorization.UserRoles.Get;

public sealed record GetUserRolesQuery(Guid ActorUserId, Guid TargetUserId);
