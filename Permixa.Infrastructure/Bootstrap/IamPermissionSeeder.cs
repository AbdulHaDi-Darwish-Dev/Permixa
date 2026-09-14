using Permixa.Application.Authorization;
using Permixa.Application.Common.Abstractions;
using Permixa.Domain.Authorization;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Bootstrap;

/// <summary>
/// Idempotently ensures the Permixa IAM permission catalog exists.
/// Does not create users, roles, or authorization state.
/// </summary>
public sealed class IamPermissionSeeder
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public IamPermissionSeeder(ApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Ensures all <see cref="IamPermissions.All"/> rows exist. Reuses existing names as-is.
    /// </summary>
    public async Task<IReadOnlyList<Permission>> EnsureAsync(CancellationToken cancellationToken = default)
    {
        var catalog = IamPermissions.All;
        var existing = await _db.Permissions
            .Where(p => catalog.Contains(p.Name))
            .ToListAsync(cancellationToken);

        var existingByName = existing.ToDictionary(p => p.Name, StringComparer.Ordinal);

        foreach (var name in catalog)
        {
            if (existingByName.ContainsKey(name))
                continue;

            var permission = Permission.Create(name, description: null, createdAtUtc: _clock.UtcNow);
            _db.Permissions.Add(permission);
            existingByName[name] = permission;
        }

        return catalog.Select(name => existingByName[name]).ToArray();
    }
}
