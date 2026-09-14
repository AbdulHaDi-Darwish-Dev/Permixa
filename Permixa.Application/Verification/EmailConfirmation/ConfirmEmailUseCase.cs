using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Verification.Abstractions;
using Permixa.Domain.Verification;
using Microsoft.Extensions.Options;

namespace Permixa.Application.Verification.EmailConfirmation;

public sealed class ConfirmEmailUseCase
{
    private readonly IVerificationChallengeRepository _challenges;
    private readonly IVerificationTokenProvider _tokens;
    private readonly IIdentityUserEmailReader _emails;
    private readonly IIdentityEmailConfirmation _confirmation;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly PermixaVerificationOptions _options;

    public ConfirmEmailUseCase(
        IVerificationChallengeRepository challenges,
        IVerificationTokenProvider tokens,
        IIdentityUserEmailReader emails,
        IIdentityEmailConfirmation confirmation,
        IUnitOfWork unitOfWork,
        IClock clock,
        IOptions<PermixaVerificationOptions> options)
    {
        _challenges = challenges;
        _tokens = tokens;
        _emails = emails;
        _confirmation = confirmation;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result> ExecuteAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ChallengeId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.VerificationValue))
        {
            return Result.Failure(VerificationErrors.ChallengeNotFound);
        }

        var challenge = await _challenges.GetByIdAsync(request.ChallengeId, cancellationToken);
        if (challenge is null || challenge.Purpose != VerificationPurpose.EmailConfirmation)
            return Result.Failure(VerificationErrors.ChallengeNotFound);

        var lifecycle = EnsureAcceptable(challenge);
        if (lifecycle.IsFailure)
            return lifecycle;

        var currentEmail = await _emails.GetEmailAsync(challenge.UserId, cancellationToken);
        if (currentEmail is null
            || !string.Equals(currentEmail, challenge.Destination, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(VerificationErrors.DestinationMismatch);
        }

        if (challenge.Method == VerificationMethod.UrlToken)
            return await ConfirmWithUrlTokenAsync(challenge, request.VerificationValue.Trim(), cancellationToken);

        if (challenge.Method == VerificationMethod.Otp)
            return await ConfirmWithOtpAsync(challenge, request.VerificationValue.Trim(), cancellationToken);

        return Result.Failure(VerificationErrors.UnsupportedMethod);
    }

    private async Task<Result> ConfirmWithUrlTokenAsync(
        VerificationChallenge challenge,
        string token,
        CancellationToken cancellationToken)
    {
        var confirmed = await _confirmation.ConfirmEmailWithTokenAsync(
            challenge.UserId,
            token,
            cancellationToken);

        if (!confirmed)
            return Result.Failure(VerificationErrors.InvalidToken);

        challenge.Consume(_clock.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result> ConfirmWithOtpAsync(
        VerificationChallenge challenge,
        string code,
        CancellationToken cancellationToken)
    {
        var valid = await _tokens.ValidateAsync(
            challenge.UserId,
            challenge.Purpose,
            challenge.Method,
            code,
            cancellationToken);

        if (!valid)
        {
            challenge.RegisterFailedAttempt();

            if (challenge.FailedAttempts >= _options.MaxOtpAttempts)
            {
                challenge.Invalidate(_clock.UtcNow);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result.Failure(VerificationErrors.TooManyAttempts);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure(VerificationErrors.InvalidCode);
        }

        await _confirmation.MarkEmailConfirmedAsync(challenge.UserId, cancellationToken);
        challenge.Consume(_clock.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private Result EnsureAcceptable(VerificationChallenge challenge)
    {
        var now = _clock.UtcNow;

        if (challenge.IsConsumed)
            return Result.Failure(VerificationErrors.AlreadyConsumed);

        if (challenge.IsInvalidated)
            return Result.Failure(VerificationErrors.Invalidated);

        if (challenge.IsExpired(now))
            return Result.Failure(VerificationErrors.Expired);

        return Result.Success();
    }
}
