using Permixa.Application.Authorization.Abstractions;
using Permixa.Domain.Authorization;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Persistence.Repositories;

public sealed class RolePermissionRepository : IRolePermissionRepository
{
    private readonly ApplicationDbContext _db;

    public RolePermissionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(
        Guid roleId,
        Guid permissionId,
        CancellationToken cancellationToken = default) =>
        _db.RolePermissions.AsNoTracking()
            .AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId, cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> GetPermissionIdsByRoleIdsAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        if (roleIds.Count == 0)
            return Array.Empty<Guid>();

        var ids = roleIds.Distinct().ToArray();

        return await _db.RolePermissions.AsNoTracking()
            .Where(rp => ids.Contains(rp.RoleId))
            .Select(rp => rp.PermissionId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<RolePermission>> GetByRoleIdAsync(
        Guid roleId,
        CancellationToken cancellationToken = default) =>
        await _db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsByRoleIdAsync(
        Guid roleId,
        CancellationToken cancellationToken = default) =>
        _db.RolePermissions.AsNoTracking()
            .AnyAsync(rp => rp.RoleId == roleId, cancellationToken);

    public Task AddAsync(RolePermission rolePermission, CancellationToken cancellationToken = default)
    {
        _db.RolePermissions.Add(rolePermission);
        return Task.CompletedTask;
    }

    public async Task RemoveAsync(
        Guid roleId,
        Guid permissionId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.RolePermissions
            .FirstOrDefaultAsync(
                rp => rp.RoleId == roleId && rp.PermissionId == permissionId,
                cancellationToken);

        if (existing is not null)
            _db.RolePermissions.Remove(existing);
    }
}
