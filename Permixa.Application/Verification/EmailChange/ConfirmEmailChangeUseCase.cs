using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Application.Verification.Abstractions;
using Permixa.Domain.Verification;

namespace Permixa.Application.Verification.EmailChange;

public sealed class ConfirmEmailChangeUseCase
{
    private readonly IVerificationChallengeRepository _challenges;
    private readonly IIdentityUserEmailReader _emails;
    private readonly IIdentityEmailChange _emailChange;
    private readonly IIdentityUserWriter _writer;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ConfirmEmailChangeUseCase(
        IVerificationChallengeRepository challenges,
        IIdentityUserEmailReader emails,
        IIdentityEmailChange emailChange,
        IIdentityUserWriter writer,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _challenges = challenges;
        _emails = emails;
        _emailChange = emailChange;
        _writer = writer;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> ExecuteAsync(
        ConfirmEmailChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ChallengeId == Guid.Empty || string.IsNullOrWhiteSpace(request.Token))
            return Result.Failure(VerificationErrors.ChallengeNotFound);

        var challenge = await _challenges.GetByIdAsync(request.ChallengeId, cancellationToken);
        if (challenge is null || challenge.Purpose != VerificationPurpose.EmailChange)
            return Result.Failure(VerificationErrors.ChallengeNotFound);

        if (challenge.Method != VerificationMethod.UrlToken)
            return Result.Failure(VerificationErrors.UnsupportedMethod);

        if (challenge.IsConsumed)
            return Result.Failure(VerificationErrors.AlreadyConsumed);

        if (challenge.IsInvalidated)
            return Result.Failure(VerificationErrors.Invalidated);

        if (challenge.IsExpired(_clock.UtcNow))
            return Result.Failure(VerificationErrors.Expired);

        var profile = await _emails.GetEmailProfileAsync(challenge.UserId, cancellationToken);
        if (profile is null)
            return Result.Failure(VerificationErrors.UserNotFound);

        if (string.IsNullOrWhiteSpace(profile.PendingEmail)
            || !string.Equals(profile.PendingEmail, challenge.Destination, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(VerificationErrors.DestinationMismatch);
        }

        if (await _emails.IsNormalizedEmailTakenByAnotherUserAsync(
                challenge.UserId,
                profile.PendingEmail,
                cancellationToken))
        {
            return Result.Failure(AuthenticationErrors.EmailAlreadyExists);
        }

        Result? outcome = null;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var changed = await _emailChange.ChangeEmailAsync(
                challenge.UserId,
                profile.PendingEmail,
                request.Token.Trim(),
                ct);

            if (!changed.Succeeded)
            {
                outcome = MapFailure(changed);
                return;
            }

            await _writer.SetPendingEmailAsync(challenge.UserId, null, ct);
            challenge.Consume(_clock.UtcNow);
            var now = _clock.UtcNow;
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.Credentials.EmailChangeConfirmed,
                    now,
                    actorUserId: challenge.UserId,
                    targetUserId: challenge.UserId),
                ct);
            await _unitOfWork.SaveChangesAsync(ct);
            outcome = Result.Success();
        }, cancellationToken);

        return outcome ?? Result.Failure(
            Error.Failure("Verification.EmailChangeFailed", "Email change confirmation failed."));
    }

    private static Result MapFailure(IdentityEmailChangeResult changed)
    {
        if (changed.UserNotFound)
            return Result.Failure(VerificationErrors.UserNotFound);

        if (changed.InvalidToken)
            return Result.Failure(VerificationErrors.InvalidToken);

        if (changed.DuplicateEmail)
            return Result.Failure(AuthenticationErrors.EmailAlreadyExists);

        return Result.Failure(VerificationErrors.InvalidToken);
    }
}
