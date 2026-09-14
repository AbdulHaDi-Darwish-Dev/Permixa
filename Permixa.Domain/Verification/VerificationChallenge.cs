using Permixa.Domain.Common;

namespace Permixa.Domain.Verification;

/// <summary>
/// Orchestration/lifecycle metadata for a verification challenge.
/// Cryptographic token generation and validation belong to ASP.NET Core Identity token providers
/// (or other application services), not this entity.
/// </summary>
public sealed class VerificationChallenge
{
    private VerificationChallenge(
        Guid id,
        Guid userId,
        VerificationPurpose purpose,
        VerificationMethod method,
        VerificationChannel channel,
        string destination,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        DateTime? consumedAtUtc,
        int failedAttempts,
        DateTime? invalidatedAtUtc)
    {
        Id = id;
        UserId = userId;
        Purpose = purpose;
        Method = method;
        Channel = channel;
        Destination = destination;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        ConsumedAtUtc = consumedAtUtc;
        FailedAttempts = failedAttempts;
        InvalidatedAtUtc = invalidatedAtUtc;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    public VerificationPurpose Purpose { get; }

    public VerificationMethod Method { get; }

    public VerificationChannel Channel { get; }

    public string Destination { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime ExpiresAtUtc { get; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTime? InvalidatedAtUtc { get; private set; }

    public bool IsExpired(DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        return now >= ExpiresAtUtc;
    }

    public bool IsConsumed => ConsumedAtUtc.HasValue;

    public bool IsInvalidated => InvalidatedAtUtc.HasValue;

    public bool IsActive(DateTime? utcNow = null) =>
        !IsConsumed && !IsInvalidated && !IsExpired(utcNow);

    public static VerificationChallenge Create(
        Guid userId,
        VerificationPurpose purpose,
        VerificationMethod method,
        VerificationChannel channel,
        string destination,
        DateTime expiresAtUtc,
        Guid? id = null,
        DateTime? createdAtUtc = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id cannot be empty.", nameof(userId));

        EnsureDefined(purpose);
        EnsureDefined(method);
        EnsureDefined(channel);

        if (string.IsNullOrWhiteSpace(destination))
            throw new DomainException("Verification destination cannot be empty.");

        var created = createdAtUtc ?? DateTime.UtcNow;

        if (expiresAtUtc <= created)
            throw new DomainException("Verification challenge expiration must be after creation.");

        return new VerificationChallenge(
            id ?? Guid.NewGuid(),
            userId,
            purpose,
            method,
            channel,
            destination.Trim(),
            created,
            expiresAtUtc,
            consumedAtUtc: null,
            failedAttempts: 0,
            invalidatedAtUtc: null);
    }

    public static VerificationChallenge Reconstitute(
        Guid id,
        Guid userId,
        VerificationPurpose purpose,
        VerificationMethod method,
        VerificationChannel channel,
        string destination,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        DateTime? consumedAtUtc,
        int failedAttempts,
        DateTime? invalidatedAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Verification challenge id cannot be empty.", nameof(id));

        if (failedAttempts < 0)
            throw new ArgumentOutOfRangeException(nameof(failedAttempts), "Failed attempts cannot be negative.");

        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        return new VerificationChallenge(
            id,
            userId,
            purpose,
            method,
            channel,
            destination,
            createdAtUtc,
            expiresAtUtc,
            consumedAtUtc,
            failedAttempts,
            invalidatedAtUtc);
    }

    /// <summary>
    /// Marks the challenge as successfully consumed. Cannot be reused.
    /// </summary>
    public void Consume(DateTime? consumedAtUtc = null)
    {
        EnsureCanTransition("consume");

        ConsumedAtUtc = consumedAtUtc ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Registers a failed verification attempt. Application decides max-attempt policy.
    /// </summary>
    public void RegisterFailedAttempt()
    {
        EnsureCanTransition("register a failed attempt");

        if (FailedAttempts == int.MaxValue)
            throw new DomainException("Failed attempts overflow.");

        FailedAttempts++;
    }

    /// <summary>
    /// Invalidates the challenge so it can no longer be used.
    /// </summary>
    public void Invalidate(DateTime? invalidatedAtUtc = null)
    {
        if (IsConsumed)
            throw new DomainException("Consumed verification challenge cannot be invalidated.");

        if (IsInvalidated)
            throw new DomainException("Verification challenge is already invalidated.");

        InvalidatedAtUtc = invalidatedAtUtc ?? DateTime.UtcNow;
    }

    private void EnsureCanTransition(string action)
    {
        if (IsConsumed)
            throw new DomainException($"Consumed verification challenge cannot {action}.");

        if (IsInvalidated)
            throw new DomainException($"Invalidated verification challenge cannot {action}.");
    }

    private static void EnsureDefined(VerificationPurpose purpose)
    {
        if (!Enum.IsDefined(purpose))
            throw new DomainException("Unknown verification purpose.");
    }

    private static void EnsureDefined(VerificationMethod method)
    {
        if (!Enum.IsDefined(method))
            throw new DomainException("Unknown verification method.");
    }

    private static void EnsureDefined(VerificationChannel channel)
    {
        if (!Enum.IsDefined(channel))
            throw new DomainException("Unknown verification channel.");
    }
}
