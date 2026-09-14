namespace Permixa.Application.Verification.EmailChange;

public sealed class ConfirmEmailChangeRequest
{
    public Guid ChallengeId { get; init; }

    public string Token { get; init; } = string.Empty;
}
