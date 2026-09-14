namespace Permixa.Application.Authentication.Mfa.BeginSetup;

public sealed record BeginAuthenticatorSetupCommand(Guid UserId);

public sealed record AuthenticatorSetupResult(
    string SharedKey,
    string AuthenticatorUri);
