namespace Permixa.Application.Authentication.Mfa.Enable;

public sealed record EnableAuthenticatorMfaCommand(Guid UserId, string TotpCode);

public sealed record EnableAuthenticatorMfaResult(IReadOnlyList<string> RecoveryCodes);
