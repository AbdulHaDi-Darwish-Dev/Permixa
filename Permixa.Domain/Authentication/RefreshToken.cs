using Permixa.Domain.Common;

namespace Permixa.Domain.Authentication;

/// <summary>
/// Refresh token used for access-token rotation.
/// Only a hash of the token is stored; never the raw token.
/// Tokens in the same <see cref="FamilyId"/> form a rotation chain for reuse detection.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken(
        Guid id,
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        DateTime? revokedAtUtc,
        Guid? replacedByTokenId)
    {
        Id = id;
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        RevokedAtUtc = revokedAtUtc;
        ReplacedByTokenId = replacedByTokenId;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    /// <summary>
    /// Stable identifier for a refresh-token rotation family (one device/session chain).
    /// </summary>
    public Guid FamilyId { get; }

    public string TokenHash { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime ExpiresAtUtc { get; }

    public DateTime? RevokedAtUtc { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public bool IsExpired(DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        return now >= ExpiresAtUtc;
    }

    public bool IsRevoked => RevokedAtUtc.HasValue;

    public bool IsActive(DateTime? utcNow = null) => !IsRevoked && !IsExpired(utcNow);

    /// <summary>
    /// True when this token was rotated away (revoked with a replacement).
    /// Presenting such a token again is treated as family-level reuse.
    /// </summary>
    public bool WasReplaced => IsRevoked && ReplacedByTokenId.HasValue;

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        DateTime expiresAtUtc,
        Guid familyId,
        Guid? id = null,
        DateTime? createdAtUtc = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id cannot be empty.", nameof(userId));

        if (familyId == Guid.Empty)
            throw new ArgumentException("Family id cannot be empty.", nameof(familyId));

        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new DomainException("Token hash cannot be empty.");

        var created = createdAtUtc ?? DateTime.UtcNow;

        if (expiresAtUtc <= created)
            throw new DomainException("Refresh token expiration must be after creation.");

        return new RefreshToken(
            id ?? Guid.NewGuid(),
            userId,
            familyId,
            tokenHash.Trim(),
            created,
            expiresAtUtc,
            revokedAtUtc: null,
            replacedByTokenId: null);
    }

    public static RefreshToken Reconstitute(
        Guid id,
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        DateTime? revokedAtUtc,
        Guid? replacedByTokenId)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Refresh token id cannot be empty.", nameof(id));

        if (familyId == Guid.Empty)
            throw new ArgumentException("Family id cannot be empty.", nameof(familyId));

        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return new RefreshToken(
            id,
            userId,
            familyId,
            tokenHash,
            createdAtUtc,
            expiresAtUtc,
            revokedAtUtc,
            replacedByTokenId);
    }

    /// <summary>
    /// Revokes this refresh token. Optionally records the replacement token id for rotation.
    /// </summary>
    public void Revoke(DateTime? revokedAtUtc = null, Guid? replacedByTokenId = null)
    {
        if (IsRevoked)
            throw new DomainException("Refresh token is already revoked.");

        RevokedAtUtc = revokedAtUtc ?? DateTime.UtcNow;
        ReplacedByTokenId = replacedByTokenId;
    }
}
