using Permixa.Application.Authorization.Abstractions;
using Permixa.Domain.Authorization;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Persistence.Repositories;

public sealed class UserPermissionOverrideRepository : IUserPermissionOverrideRepository
{
    private readonly ApplicationDbContext _db;

    public UserPermissionOverrideRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<UserPermissionOverride?> GetAsync(
        Guid userId,
        Guid permissionId,
        CancellationToken cancellationToken = default) =>
        // Tracked: Set/Remove use cases mutate Effect or remove the entity.
        _db.UserPermissionOverrides
            .FirstOrDefaultAsync(
                o => o.UserId == userId && o.PermissionId == permissionId,
                cancellationToken);

    public async Task<IReadOnlyCollection<UserPermissionOverride>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _db.UserPermissionOverrides.AsNoTracking()
            .Where(o => o.UserId == userId)
            .ToListAsync(cancellationToken);

    public Task AddAsync(
        UserPermissionOverride permissionOverride,
        CancellationToken cancellationToken = default)
    {
        _db.UserPermissionOverrides.Add(permissionOverride);
        return Task.CompletedTask;
    }

    public async Task RemoveAsync(
        Guid userId,
        Guid permissionId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.UserPermissionOverrides
            .FirstOrDefaultAsync(
                o => o.UserId == userId && o.PermissionId == permissionId,
                cancellationToken);

        if (existing is not null)
            _db.UserPermissionOverrides.Remove(existing);
    }
}
