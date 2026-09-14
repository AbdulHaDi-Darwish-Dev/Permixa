namespace Permixa.Application.Authorization.Roles.Rename;

public sealed record RenameRoleCommand(Guid ActorUserId, Guid RoleId, string Name);
