using Permixa.Application.Authorization.Sessions;
using Permixa.Application.Authorization.Sessions.Models;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQL-grouped refresh-family projections. Does not introduce a Session table.
/// </summary>
public sealed class SessionReader : ISessionReader
{
    private readonly ApplicationDbContext _db;

    public SessionReader(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SessionFamilyRecord>> GetActiveFamiliesForUserAsync(
        Guid userId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var active = await _db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > utcNow)
            .GroupBy(t => t.FamilyId)
            .Select(g => new { FamilyId = g.Key, ExpiresAtUtc = g.Max(t => t.ExpiresAtUtc) })
            .ToListAsync(cancellationToken);

        if (active.Count == 0)
            return [];

        var familyIds = active.Select(a => a.FamilyId).ToArray();
        var created = await _db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && familyIds.Contains(t.FamilyId))
            .GroupBy(t => t.FamilyId)
            .Select(g => new { FamilyId = g.Key, CreatedAtUtc = g.Min(t => t.CreatedAtUtc) })
            .ToListAsync(cancellationToken);

        var expiresByFamily = active.ToDictionary(a => a.FamilyId, a => a.ExpiresAtUtc);
        return created
            .Select(c => new SessionFamilyRecord(c.FamilyId, c.CreatedAtUtc, expiresByFamily[c.FamilyId]))
            .OrderByDescending(s => s.CreatedAtUtc)
            .ThenBy(s => s.FamilyId)
            .ToList();
    }
}
