using Permixa.Application.Common.Paging;
using Permixa.Application.Identity.Abstractions;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Identity;

/// <summary>
/// Read-only Identity user queries via EF Core (set-based, no UserManager for pure reads).
/// </summary>
public sealed class IdentityUserReader : IIdentityUserReader
{
    private readonly ApplicationDbContext _db;

    public IdentityUserReader(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> GetUserRoleIdsAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);

    public async Task<IdentityAccountState?> GetAccountStateAsync(
        Guid userId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var now = ToUtcOffset(utcNow);
        return await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new IdentityAccountState(
                u.Id,
                u.IsDisabled,
                u.LockoutEnabled && u.LockoutEnd != null && u.LockoutEnd > now))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IdentityMfaLoginGate?> GetMfaLoginGateAsync(
        Guid userId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var now = ToUtcOffset(utcNow);
        return await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new IdentityMfaLoginGate(
                u.Id,
                u.IsDisabled,
                u.LockoutEnabled && u.LockoutEnd != null && u.LockoutEnd > now,
                u.EmailConfirmed,
                u.TwoFactorEnabled))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IdentityUserIamRecord?> GetIamUserByIdAsync(
        Guid userId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var now = ToUtcOffset(utcNow);
        var levels =
            from ur in _db.UserRoles.AsNoTracking()
            join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            group r.RoleLevel by ur.UserId
            into g
            select new { UserId = g.Key, Level = g.Min() };

        var row = await (
                from u in _db.Users.AsNoTracking()
                join lv in levels on u.Id equals lv.UserId into lvg
                from lv in lvg.DefaultIfEmpty()
                where u.Id == userId
                select new
                {
                    u.Id,
                    u.UserName,
                    u.Email,
                    u.EmailConfirmed,
                    u.IsDisabled,
                    IsLocked = u.LockoutEnabled && u.LockoutEnd != null && u.LockoutEnd > now,
                    u.LockoutEnd,
                    Level = (int?)lv.Level,
                    u.TwoFactorEnabled,
                    u.PendingEmail
                })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new IdentityUserIamRecord(
                row.Id,
                row.UserName,
                row.Email,
                row.EmailConfirmed,
                row.IsDisabled,
                row.IsLocked,
                row.LockoutEnd?.UtcDateTime,
                row.Level,
                row.TwoFactorEnabled,
                row.PendingEmail);
    }

    public async Task<IdentityUserIamRecord?> GetIamUserByEmailAsync(
        string email,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToUpperInvariant();
        var userId = await _db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalized)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return userId is null
            ? null
            : await GetIamUserByIdAsync(userId.Value, utcNow, cancellationToken);
    }

    public Task<bool> HasRoleNameAsync(
        Guid userId,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        var normalized = roleName.Trim().ToUpperInvariant();
        return (
                from ur in _db.UserRoles.AsNoTracking()
                join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where ur.UserId == userId && r.NormalizedName == normalized
                select ur.UserId)
            .AnyAsync(cancellationToken);
    }

    public async Task<PagedResult<IdentityUserIamRecord>> SearchManageableUsersAsync(
        IdentityUserSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = ToUtcOffset(query.UtcNow);
        var levels =
            from ur in _db.UserRoles.AsNoTracking()
            join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            group r.RoleLevel by ur.UserId
            into g
            select new { UserId = g.Key, Level = g.Min() };

        var source =
            from u in _db.Users.AsNoTracking()
            join lv in levels on u.Id equals lv.UserId into lvg
            from lv in lvg.DefaultIfEmpty()
            select new { User = u, Level = (int?)lv.Level };

        source = source.Where(x =>
            x.User.Id != query.ActorUserId
            && (x.Level == null || x.Level > query.ActorEffectiveLevel));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(x =>
                (x.User.Email != null && x.User.Email.Contains(term))
                || (x.User.UserName != null && x.User.UserName.Contains(term)));
        }

        if (query.IsDisabled is bool disabled)
            source = source.Where(x => x.User.IsDisabled == disabled);

        if (query.IsLocked is bool locked)
        {
            source = locked
                ? source.Where(x =>
                    x.User.LockoutEnabled && x.User.LockoutEnd != null && x.User.LockoutEnd > now)
                : source.Where(x =>
                    !(x.User.LockoutEnabled && x.User.LockoutEnd != null && x.User.LockoutEnd > now));
        }

        var ordered = source
            .OrderBy(x => x.User.UserName)
            .ThenBy(x => x.User.Id);

        var total = await ordered.CountAsync(cancellationToken);
        var skip = (query.Page - 1) * query.PageSize;

        var page = await ordered
            .Skip(skip)
            .Take(query.PageSize)
            .Select(x => new IdentityUserIamRecord(
                x.User.Id,
                x.User.UserName,
                x.User.Email,
                x.User.EmailConfirmed,
                x.User.IsDisabled,
                x.User.LockoutEnabled && x.User.LockoutEnd != null && x.User.LockoutEnd > now,
                x.User.LockoutEnd == null ? null : x.User.LockoutEnd.Value.UtcDateTime,
                x.Level,
                x.User.TwoFactorEnabled,
                x.User.PendingEmail))
            .ToListAsync(cancellationToken);

        return new PagedResult<IdentityUserIamRecord>(page, query.Page, query.PageSize, total);
    }

    private static DateTimeOffset ToUtcOffset(DateTime utcNow) =>
        new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
}
