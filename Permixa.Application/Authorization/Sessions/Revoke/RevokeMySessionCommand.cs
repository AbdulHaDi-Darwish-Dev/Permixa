namespace Permixa.Application.Authorization.Sessions.Revoke;

public sealed record RevokeMySessionCommand(Guid UserId, Guid FamilyId);
