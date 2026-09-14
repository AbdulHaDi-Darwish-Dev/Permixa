namespace Permixa.Application.Authorization.Users.Create;

public sealed record AdminCreateUserCommand(
    Guid ActorUserId,
    string Email,
    string UserName,
    string Password);
