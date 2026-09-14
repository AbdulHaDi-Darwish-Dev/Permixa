namespace Permixa.Application.Authorization.Users.Lock;

public sealed record UnlockUserCommand(Guid ActorUserId, Guid TargetUserId);
