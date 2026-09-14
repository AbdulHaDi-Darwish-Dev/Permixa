namespace Permixa.Application.Authorization.Users.Disable;

public sealed record EnableUserCommand(Guid ActorUserId, Guid TargetUserId);
