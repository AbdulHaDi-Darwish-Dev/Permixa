using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Verification.Abstractions;
using Permixa.Application.Verification.Models;
using Permixa.Domain.Verification;
using Microsoft.Extensions.Options;

namespace Permixa.Application.Verification;

/// <summary>
/// Shared challenge lifecycle: cooldown, invalidate-open, persist-before-dispatch.
/// </summary>
public sealed class VerificationChallengeIssuer
{
    private readonly IVerificationChallengeRepository _challenges;
    private readonly IVerificationTokenProvider _tokens;
    private readonly IVerificationDispatcher _dispatcher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IPersistenceExceptionClassifier _persistence;
    private readonly PermixaVerificationOptions _options;

    public VerificationChallengeIssuer(
        IVerificationChallengeRepository challenges,
        IVerificationTokenProvider tokens,
        IVerificationDispatcher dispatcher,
        IUnitOfWork unitOfWork,
        IClock clock,
        IPersistenceExceptionClassifier persistence,
        IOptions<PermixaVerificationOptions> options)
    {
        _challenges = challenges;
        _tokens = tokens;
        _dispatcher = dispatcher;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _persistence = persistence;
        _options = options.Value;
    }

    /// <summary>
    /// Issues a challenge for authenticated / identity-known flows. Surfaces cooldown and unique conflicts.
    /// </summary>
    public async Task<Result<Guid>> IssueAsync(
        Guid userId,
        VerificationPurpose purpose,
        VerificationMethod method,
        VerificationChannel channel,
        string destination,
        CancellationToken cancellationToken = default)
    {
        var outcome = await IssueCoreAsync(
            userId,
            purpose,
            method,
            channel,
            destination,
            suppressDistinguishingErrors: false,
            cancellationToken);

        return outcome.Status switch
        {
            IssueStatus.Issued => Result.Success(outcome.ChallengeId!.Value),
            IssueStatus.ResendTooSoon => Result.Failure<Guid>(VerificationErrors.ResendTooSoon),
            IssueStatus.Conflict => Result.Failure<Guid>(VerificationErrors.RequestConflict),
            _ => Result.Failure<Guid>(VerificationErrors.RequestConflict)
        };
    }

    /// <summary>
    /// Public anti-enumeration issue path. Always returns success to the caller; may no-op internally.
    /// </summary>
    public async Task IssueBestEffortAntiEnumerationAsync(
        Guid userId,
        VerificationPurpose purpose,
        VerificationMethod method,
        VerificationChannel channel,
        string destination,
        CancellationToken cancellationToken = default)
    {
        await IssueCoreAsync(
            userId,
            purpose,
            method,
            channel,
            destination,
            suppressDistinguishingErrors: true,
            cancellationToken);
    }

    private async Task<IssueOutcome> IssueCoreAsync(
        Guid userId,
        VerificationPurpose purpose,
        VerificationMethod method,
        VerificationChannel channel,
        string destination,
        bool suppressDistinguishingErrors,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var open = await _challenges.GetOpenAsync(userId, purpose, destination, cancellationToken);

        var newestActive = open
            .Where(c => c.IsActive(now))
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefault();

        if (newestActive is not null
            && now - newestActive.CreatedAtUtc < _options.ResendCooldown)
        {
            return suppressDistinguishingErrors
                ? IssueOutcome.Suppressed()
                : IssueOutcome.TooSoon();
        }

        var rawToken = await _tokens.GenerateAsync(userId, purpose, method, cancellationToken);

        var invalidatedAny = false;
        foreach (var existing in open)
        {
            if (!existing.IsConsumed && !existing.IsInvalidated)
            {
                existing.Invalidate(now);
                invalidatedAny = true;
            }
        }

        // Persist invalidations before insert so the filtered unique index does not see
        // a still-open predecessor when EF orders INSERT ahead of UPDATE.
        if (invalidatedAny)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        var lifetime = method == VerificationMethod.Otp
            ? _options.OtpLifetime
            : _options.UrlTokenLifetime;

        var challenge = VerificationChallenge.Create(
            userId,
            purpose,
            method,
            channel,
            destination,
            expiresAtUtc: now.Add(lifetime),
            createdAtUtc: now);

        await _challenges.AddAsync(challenge, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (_persistence.IsUniqueConstraintViolation(ex))
        {
            return suppressDistinguishingErrors
                ? IssueOutcome.Suppressed()
                : IssueOutcome.Conflicted();
        }

        var delivery = new VerificationDeliveryRequest(
            challenge.Id,
            destination,
            purpose,
            method,
            channel,
            rawToken,
            challenge.ExpiresAtUtc);

        try
        {
            await _dispatcher.DispatchAsync(delivery, cancellationToken);
        }
        catch
        {
            await TryInvalidateAfterDispatchFailureAsync(challenge, cancellationToken);
            throw;
        }

        return IssueOutcome.Created(challenge.Id);
    }

    private async Task TryInvalidateAfterDispatchFailureAsync(
        VerificationChallenge challenge,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!challenge.IsConsumed && !challenge.IsInvalidated)
            {
                challenge.Invalidate(_clock.UtcNow);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        catch
        {
            // Best-effort compensation; original dispatch failure is rethrown by caller.
        }
    }

    private enum IssueStatus
    {
        Issued,
        ResendTooSoon,
        Conflict,
        Suppressed
    }

    private sealed class IssueOutcome
    {
        private IssueOutcome(IssueStatus status, Guid? challengeId)
        {
            Status = status;
            ChallengeId = challengeId;
        }

        public IssueStatus Status { get; }

        public Guid? ChallengeId { get; }

        public static IssueOutcome Created(Guid id) => new(IssueStatus.Issued, id);

        public static IssueOutcome TooSoon() => new(IssueStatus.ResendTooSoon, null);

        public static IssueOutcome Conflicted() => new(IssueStatus.Conflict, null);

        public static IssueOutcome Suppressed() => new(IssueStatus.Suppressed, null);
    }
}
