namespace Permixa.Application.Authentication.Mfa.GetMfaStatus;

public sealed record GetMfaStatusQuery(Guid UserId);

public sealed record MfaStatusResult(
    bool IsEnabled,
    bool HasAuthenticatorKey,
    int RecoveryCodesRemaining);
