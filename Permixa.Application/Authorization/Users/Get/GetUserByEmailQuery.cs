namespace Permixa.Application.Authorization.Users.Get;

public sealed record GetUserByEmailQuery(Guid ActorUserId, string Email);
