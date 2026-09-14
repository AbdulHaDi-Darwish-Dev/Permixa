namespace Permixa.Application.Authentication.Mfa.Disable;

public sealed record DisableMfaCommand(Guid UserId, string CurrentPassword);
