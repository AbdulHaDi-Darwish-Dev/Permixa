using Permixa.Domain.Authentication;

namespace Permixa.Application.Authentication.Abstractions;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    Task<RefreshToken?> GetByIdAsync(
        Guid refreshTokenId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Active (non-revoked) tokens in a family. Tracked for mutation.
    /// </summary>
    Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyIdAsync(
        Guid familyId,
        CancellationToken cancellationToken = default);

    Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every non-revoked refresh token for the user in one set-based update.
    /// </summary>
    Task<int> RevokeAllForUserAsync(
        Guid userId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the family has at least one refresh-token row owned by the user.
    /// Does not distinguish other users' families.
    /// </summary>
    Task<bool> FamilyExistsForUserAsync(
        Guid userId,
        Guid familyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every non-revoked refresh token in the family that belongs to the user.
    /// </summary>
    Task<int> RevokeFamilyForUserAsync(
        Guid userId,
        Guid familyId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the family has at least one non-revoked, non-expired token owned by the user.
    /// </summary>
    Task<bool> HasActiveFamilyForUserAsync(
        Guid userId,
        Guid familyId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every non-revoked refresh token for the user except the given family.
    /// </summary>
    Task<int> RevokeAllForUserExceptFamilyAsync(
        Guid userId,
        Guid currentFamilyId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken = default);
}
