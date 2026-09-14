namespace Permixa.Application.Authentication.Mfa.Complete;

public sealed class CompleteMfaWithRecoveryCodeRequest
{
    public string MfaProof { get; init; } = string.Empty;

    public string RecoveryCode { get; init; } = string.Empty;
}
