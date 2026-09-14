using Permixa.Domain.Verification;

namespace Permixa.Application.Verification.Abstractions;

public interface IVerificationChallengeRepository
{
    Task<VerificationChallenge?> GetByIdAsync(
        Guid challengeId,
        CancellationToken cancellationToken = default);

    Task<VerificationChallenge?> GetActiveAsync(
        Guid userId,
        VerificationPurpose purpose,
        string destination,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Open = ConsumedAtUtc IS NULL AND InvalidatedAtUtc IS NULL (includes expired unresolved rows).
    /// </summary>
    Task<IReadOnlyList<VerificationChallenge>> GetOpenAsync(
        Guid userId,
        VerificationPurpose purpose,
        string destination,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        VerificationChallenge challenge,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates every open challenge for the user and purpose, regardless of destination.
    /// </summary>
    Task<int> InvalidateOpenForPurposeAsync(
        Guid userId,
        VerificationPurpose purpose,
        DateTime invalidatedAtUtc,
        CancellationToken cancellationToken = default);
}
