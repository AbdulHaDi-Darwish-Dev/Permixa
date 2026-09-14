namespace Permixa.Application.Authorization.Roles.Delete;

public sealed record DeleteRoleCommand(Guid ActorUserId, Guid RoleId);
