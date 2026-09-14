namespace Permixa.Application.Authorization.UserRoles.Remove;

public sealed record RemoveRoleFromUserCommand(Guid ActorUserId, Guid TargetUserId, Guid RoleId);
