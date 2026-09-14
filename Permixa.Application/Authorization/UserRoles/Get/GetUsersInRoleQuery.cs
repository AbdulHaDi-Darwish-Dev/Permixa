namespace Permixa.Application.Authorization.UserRoles.Get;

public sealed record GetUsersInRoleQuery(Guid ActorUserId, Guid RoleId);
