namespace Permixa.Application.Authorization.Sessions.Revoke;

public sealed record RevokeAllUserSessionsCommand(Guid ActorUserId, Guid TargetUserId);
