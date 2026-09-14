namespace Permixa.Application.Authentication.Mfa.Complete;

public sealed class CompleteMfaWithTotpRequest
{
    public string MfaProof { get; init; } = string.Empty;

    public string TotpCode { get; init; } = string.Empty;
}
