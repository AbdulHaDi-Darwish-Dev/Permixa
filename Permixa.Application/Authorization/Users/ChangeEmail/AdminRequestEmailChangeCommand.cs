namespace Permixa.Application.Authorization.Users.ChangeEmail;

public sealed record AdminRequestEmailChangeCommand(
    Guid ActorUserId,
    Guid TargetUserId,
    string NewEmail);
