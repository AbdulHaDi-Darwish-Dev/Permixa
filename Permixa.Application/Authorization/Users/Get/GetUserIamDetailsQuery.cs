namespace Permixa.Application.Authorization.Users.Get;

public sealed record GetUserIamDetailsQuery(Guid ActorUserId, Guid TargetUserId);
