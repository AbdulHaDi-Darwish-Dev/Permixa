using Permixa.Domain.Common;

namespace Permixa.Domain.Authentication;

/// <summary>
/// Server-side pending MFA login challenge. Stores only a proof hash; never the raw proof.
/// One row per user; a new password login replaces the current proof in place.
/// </summary>
public sealed class MfaLoginChallenge
{
    private MfaLoginChallenge(
        Guid id,
        Guid userId,
        string proofHash,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        int attemptCount,
        DateTime? consumedAtUtc)
    {
        Id = id;
        UserId = userId;
        ProofHash = proofHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        AttemptCount = attemptCount;
        ConsumedAtUtc = consumedAtUtc;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    public string ProofHash { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public bool IsConsumed => ConsumedAtUtc.HasValue;

    public bool IsExpired(DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        return now >= ExpiresAtUtc;
    }

    public bool IsActive(DateTime? utcNow = null) =>
        !IsConsumed && !IsExpired(utcNow);

    public static MfaLoginChallenge Create(
        Guid userId,
        string proofHash,
        DateTime expiresAtUtc,
        Guid? id = null,
        DateTime? createdAtUtc = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id cannot be empty.", nameof(userId));

        if (string.IsNullOrWhiteSpace(proofHash))
            throw new DomainException("Proof hash cannot be empty.");

        var created = createdAtUtc ?? DateTime.UtcNow;
        if (expiresAtUtc <= created)
            throw new DomainException("MFA challenge expiration must be after creation.");

        return new MfaLoginChallenge(
            id ?? Guid.NewGuid(),
            userId,
            proofHash.Trim(),
            created,
            expiresAtUtc,
            attemptCount: 0,
            consumedAtUtc: null);
    }

    public static MfaLoginChallenge Reconstitute(
        Guid id,
        Guid userId,
        string proofHash,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        int attemptCount,
        DateTime? consumedAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Challenge id cannot be empty.", nameof(id));

        if (userId == Guid.Empty)
            throw new ArgumentException("User id cannot be empty.", nameof(userId));

        ArgumentException.ThrowIfNullOrWhiteSpace(proofHash);

        return new MfaLoginChallenge(
            id,
            userId,
            proofHash,
            createdAtUtc,
            expiresAtUtc,
            attemptCount,
            consumedAtUtc);
    }

    public void Replace(string proofHash, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(proofHash))
            throw new DomainException("Proof hash cannot be empty.");

        if (expiresAtUtc <= createdAtUtc)
            throw new DomainException("MFA challenge expiration must be after creation.");

        ProofHash = proofHash.Trim();
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        AttemptCount = 0;
        ConsumedAtUtc = null;
    }

    public void Consume(DateTime? consumedAtUtc = null)
    {
        if (IsConsumed)
            throw new DomainException("MFA login challenge is already consumed.");

        ConsumedAtUtc = consumedAtUtc ?? DateTime.UtcNow;
    }

    public void RegisterFailedAttempt()
    {
        if (IsConsumed)
            throw new DomainException("Consumed MFA login challenge cannot accept attempts.");

        checked
        {
            AttemptCount++;
        }
    }
}
