using Permixa.Domain.Verification;

namespace Permixa.Application.Verification.EmailConfirmation;

public sealed class RequestEmailConfirmationRequest
{
    public required Guid UserId { get; init; }

    public required VerificationMethod Method { get; init; }
}

public sealed class RequestEmailConfirmationResult
{
    public required Guid? ChallengeId { get; init; }

    public required bool AlreadyConfirmed { get; init; }

    public static RequestEmailConfirmationResult Issued(Guid challengeId) =>
        new() { ChallengeId = challengeId, AlreadyConfirmed = false };

    public static RequestEmailConfirmationResult ConfirmedAlready() =>
        new() { ChallengeId = null, AlreadyConfirmed = true };
}

public sealed class ConfirmEmailRequest
{
    public required Guid ChallengeId { get; init; }

    public required string VerificationValue { get; init; }
}
