using Permixa.Application.Authentication.Abstractions;
using Permixa.Domain.Authentication;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Persistence.Repositories;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ApplicationDbContext _db;

    public RefreshTokenRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<RefreshToken?> GetByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default) =>
        _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public Task<RefreshToken?> GetByIdAsync(
        Guid refreshTokenId,
        CancellationToken cancellationToken = default) =>
        _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Id == refreshTokenId, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyIdAsync(
        Guid familyId,
        CancellationToken cancellationToken = default) =>
        await _db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

    public Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default)
    {
        _db.RefreshTokens.Add(refreshToken);
        return Task.CompletedTask;
    }

    public Task<int> RevokeAllForUserAsync(
        Guid userId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken = default) =>
        _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(t => t.RevokedAtUtc, revokedAtUtc),
                cancellationToken);

    public Task<bool> FamilyExistsForUserAsync(
        Guid userId,
        Guid familyId,
        CancellationToken cancellationToken = default) =>
        _db.RefreshTokens.AsNoTracking()
            .AnyAsync(t => t.UserId == userId && t.FamilyId == familyId, cancellationToken);

    public Task<int> RevokeFamilyForUserAsync(
        Guid userId,
        Guid familyId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken = default) =>
        _db.RefreshTokens
            .Where(t => t.UserId == userId && t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(t => t.RevokedAtUtc, revokedAtUtc),
                cancellationToken);

    public Task<bool> HasActiveFamilyForUserAsync(
        Guid userId,
        Guid familyId,
        DateTime utcNow,
        CancellationToken cancellationToken = default) =>
        _db.RefreshTokens.AsNoTracking()
            .AnyAsync(
                t => t.UserId == userId
                     && t.FamilyId == familyId
                     && t.RevokedAtUtc == null
                     && t.ExpiresAtUtc > utcNow,
                cancellationToken);

    public Task<int> RevokeAllForUserExceptFamilyAsync(
        Guid userId,
        Guid currentFamilyId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken = default) =>
        _db.RefreshTokens
            .Where(t =>
                t.UserId == userId
                && t.FamilyId != currentFamilyId
                && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(t => t.RevokedAtUtc, revokedAtUtc),
                cancellationToken);
}
