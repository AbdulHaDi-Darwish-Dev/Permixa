namespace Permixa.Application.Authorization.Roles.Get;

public sealed record GetRoleByIdQuery(Guid ActorUserId, Guid RoleId);
