using Permixa.Application.Identity.Abstractions;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Identity;

/// <summary>
/// Read-only role queries via EF Core. RoleManager is unnecessary for this contract.
/// </summary>
public sealed class IdentityRoleReader : IIdentityRoleReader
{
    private readonly ApplicationDbContext _db;

    public IdentityRoleReader(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<bool> RoleExistsAsync(Guid roleId, CancellationToken cancellationToken = default) =>
        _db.Roles.AsNoTracking().AnyAsync(r => r.Id == roleId, cancellationToken);

    public async Task<IdentityRoleRecord?> GetByIdAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Roles.AsNoTracking()
            .Where(r => r.Id == roleId)
            .Select(r => new IdentityRoleRecord(r.Id, r.Name!, r.RoleLevel))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IdentityRoleRecord?> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return await _db.Roles.AsNoTracking()
            .Where(r => r.NormalizedName == normalized)
            .Select(r => new IdentityRoleRecord(r.Id, r.Name!, r.RoleLevel))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IdentityRoleRecord>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.Roles.AsNoTracking()
            .Select(r => new IdentityRoleRecord(r.Id, r.Name!, r.RoleLevel))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasAssignedUsersAsync(
        Guid roleId,
        CancellationToken cancellationToken = default) =>
        _db.UserRoles.AsNoTracking().AnyAsync(ur => ur.RoleId == roleId, cancellationToken);
}
