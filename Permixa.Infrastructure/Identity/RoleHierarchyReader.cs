using Permixa.Application.Authorization.Abstractions;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Identity;

/// <summary>
/// Set-based RoleLevel reads from ApplicationRole / UserRoles.
/// </summary>
public sealed class RoleHierarchyReader : IRoleHierarchyReader
{
    private readonly ApplicationDbContext _db;

    public RoleHierarchyReader(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<int?> GetEffectiveUserLevelAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // MIN(RoleLevel) among assigned roles — smaller = higher authority.
        // No roles → null (not 0).
        return await (
                from ur in _db.UserRoles.AsNoTracking()
                join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where ur.UserId == userId
                select (int?)r.RoleLevel)
            .MinAsync(cancellationToken);
    }

    public async Task<int?> GetRoleLevelAsync(
        Guid roleId,
        CancellationToken cancellationToken = default) =>
        await _db.Roles.AsNoTracking()
            .Where(r => r.Id == roleId)
            .Select(r => (int?)r.RoleLevel)
            .FirstOrDefaultAsync(cancellationToken);
}
