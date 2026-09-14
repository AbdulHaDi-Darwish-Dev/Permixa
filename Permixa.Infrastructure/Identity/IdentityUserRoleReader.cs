using Permixa.Application.Identity.Abstractions;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Identity;

public sealed class IdentityUserRoleReader : IIdentityUserRoleReader
{
    private readonly ApplicationDbContext _db;

    public IdentityUserRoleReader(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<bool> IsInRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default) =>
        _db.UserRoles.AsNoTracking()
            .AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId, cancellationToken);

    public async Task<IReadOnlyList<IdentityRoleRecord>> GetRolesForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await (
                from ur in _db.UserRoles.AsNoTracking()
                join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where ur.UserId == userId
                select new IdentityRoleRecord(r.Id, r.Name!, r.RoleLevel))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IamUserRecord>> GetUsersInRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        return await (
                from ur in _db.UserRoles.AsNoTracking()
                join u in _db.Users.AsNoTracking() on ur.UserId equals u.Id
                where ur.RoleId == roleId
                select new IamUserRecord(u.Id, u.UserName, u.Email))
            .ToListAsync(cancellationToken);
    }
}
