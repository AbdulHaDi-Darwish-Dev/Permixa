namespace Permixa.Application.Authorization.Users.Lock;

public sealed record LockUserCommand(Guid ActorUserId, Guid TargetUserId, DateTime LockedUntilUtc);
