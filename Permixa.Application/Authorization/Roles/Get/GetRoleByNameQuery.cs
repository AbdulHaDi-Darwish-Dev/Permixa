namespace Permixa.Application.Authorization.Roles.Get;

public sealed record GetRoleByNameQuery(Guid ActorUserId, string Name);
