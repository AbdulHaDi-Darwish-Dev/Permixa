using Permixa.Application.Authentication;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Verification.Abstractions;
using Permixa.Domain.Verification;

namespace Permixa.Application.Verification.PasswordReset;

public sealed class ResetPasswordWithVerificationUseCase
{
    private readonly IVerificationChallengeRepository _challenges;
    private readonly IIdentityUserEmailReader _emails;
    private readonly IIdentityPasswordReset _passwordReset;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ResetPasswordWithVerificationUseCase(
        IVerificationChallengeRepository challenges,
        IIdentityUserEmailReader emails,
        IIdentityPasswordReset passwordReset,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _challenges = challenges;
        _emails = emails;
        _passwordReset = passwordReset;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> ExecuteAsync(
        ResetPasswordWithVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ChallengeId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Token)
            || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return Result.Failure(VerificationErrors.ChallengeNotFound);
        }

        var challenge = await _challenges.GetByIdAsync(request.ChallengeId, cancellationToken);
        if (challenge is null || challenge.Purpose != VerificationPurpose.PasswordReset)
            return Result.Failure(VerificationErrors.ChallengeNotFound);

        if (challenge.Method != VerificationMethod.UrlToken)
            return Result.Failure(VerificationErrors.UnsupportedMethod);

        if (challenge.IsConsumed)
            return Result.Failure(VerificationErrors.AlreadyConsumed);

        if (challenge.IsInvalidated)
            return Result.Failure(VerificationErrors.Invalidated);

        if (challenge.IsExpired(_clock.UtcNow))
            return Result.Failure(VerificationErrors.Expired);

        var currentEmail = await _emails.GetEmailAsync(challenge.UserId, cancellationToken);
        if (currentEmail is null
            || !string.Equals(currentEmail, challenge.Destination, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(VerificationErrors.DestinationMismatch);
        }

        var reset = await _passwordReset.ResetPasswordAsync(
            challenge.UserId,
            request.Token.Trim(),
            request.NewPassword,
            cancellationToken);

        if (!reset.Succeeded)
        {
            if (reset.InvalidPassword)
                return Result.Failure(AuthenticationErrors.InvalidPassword);

            if (reset.InvalidToken)
                return Result.Failure(VerificationErrors.InvalidToken);

            return Result.Failure(VerificationErrors.InvalidToken);
        }

        challenge.Consume(_clock.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
