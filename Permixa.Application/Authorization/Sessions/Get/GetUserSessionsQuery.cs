namespace Permixa.Application.Authorization.Sessions.Get;

public sealed record GetUserSessionsQuery(Guid ActorUserId, Guid TargetUserId);
