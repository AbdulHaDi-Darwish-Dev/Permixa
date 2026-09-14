namespace Permixa.Application.Authentication.Mfa.Regenerate;

public sealed record RegenerateRecoveryCodesCommand(Guid UserId, string CurrentPassword);

public sealed record RegenerateRecoveryCodesResult(IReadOnlyList<string> RecoveryCodes);
