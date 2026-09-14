namespace Permixa.Application.Authorization.Users.Disable;

public sealed record DisableUserCommand(Guid ActorUserId, Guid TargetUserId);
