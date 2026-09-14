using Permixa.Application.Authorization.Abstractions;
using Permixa.Domain.Authorization;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Persistence.Repositories;

public sealed class PermissionRepository : IPermissionRepository
{
    private readonly ApplicationDbContext _db;

    public PermissionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<Permission?> GetByIdAsync(Guid permissionId, CancellationToken cancellationToken = default) =>
        // Tracked: UpdatePermissionDescription mutates Description on the same instance.
        _db.Permissions.FirstOrDefaultAsync(p => p.Id == permissionId, cancellationToken);

    public async Task<IReadOnlyCollection<Permission>> GetByIdsAsync(
        IReadOnlyCollection<Guid> permissionIds,
        CancellationToken cancellationToken = default)
    {
        if (permissionIds.Count == 0)
            return Array.Empty<Permission>();

        var ids = permissionIds.Distinct().ToArray();

        return await _db.Permissions.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }

    public Task<Permission?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        _db.Permissions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == name, cancellationToken);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
        _db.Permissions.AsNoTracking()
            .AnyAsync(p => p.Name == name, cancellationToken);

    public async Task<IReadOnlyCollection<Permission>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _db.Permissions.AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Permission>> GetByRoleIdsAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        if (roleIds.Count == 0)
            return Array.Empty<Permission>();

        var ids = roleIds.Distinct().ToArray();

        return await (
                from rp in _db.RolePermissions.AsNoTracking()
                join p in _db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                where ids.Contains(rp.RoleId)
                select p)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(Permission permission, CancellationToken cancellationToken = default)
    {
        _db.Permissions.Add(permission);
        return Task.CompletedTask;
    }
}
