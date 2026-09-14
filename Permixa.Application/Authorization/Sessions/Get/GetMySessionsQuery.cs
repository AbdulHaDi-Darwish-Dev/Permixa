namespace Permixa.Application.Authorization.Sessions.Get;

public sealed record GetMySessionsQuery(Guid UserId, Guid? CurrentFamilyId = null);
