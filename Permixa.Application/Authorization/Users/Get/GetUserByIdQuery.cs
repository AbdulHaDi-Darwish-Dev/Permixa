namespace Permixa.Application.Authorization.Users.Get;

public sealed record GetUserByIdQuery(Guid ActorUserId, Guid TargetUserId);
