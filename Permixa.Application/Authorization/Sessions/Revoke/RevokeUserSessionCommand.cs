namespace Permixa.Application.Authorization.Sessions.Revoke;

public sealed record RevokeUserSessionCommand(Guid ActorUserId, Guid TargetUserId, Guid FamilyId);
