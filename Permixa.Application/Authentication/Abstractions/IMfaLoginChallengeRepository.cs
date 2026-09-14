using Permixa.Domain.Authentication;

namespace Permixa.Application.Authentication.Abstractions;

public interface IMfaLoginChallengeRepository
{
    Task<MfaLoginChallenge?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<MfaLoginChallenge?> GetByProofHashAsync(
        string proofHash,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        MfaLoginChallenge challenge,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments <see cref="MfaLoginChallenge.AttemptCount"/> for an unconsumed challenge in one update.
    /// </summary>
    Task<int> IncrementAttemptsAsync(
        Guid challengeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes an unconsumed challenge in one update. Returns the number of rows affected.
    /// </summary>
    Task<int> ConsumeUnconsumedAsync(
        Guid challengeId,
        DateTime consumedAtUtc,
        CancellationToken cancellationToken = default);
}
