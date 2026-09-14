namespace Permixa.Application.Authorization.UserRoles.Assign;

public sealed record AssignRoleToUserCommand(Guid ActorUserId, Guid TargetUserId, Guid RoleId);
