namespace Permixa.Application.Authorization.Users.ForcePasswordReset;

public sealed record ForcePasswordResetCommand(
    Guid ActorUserId,
    Guid TargetUserId);
