using Permixa.Application.Identity.Abstractions;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Identity;

/// <summary>
/// Mutates ApplicationUser.AuthorizationVersion without calling SaveChanges.
/// </summary>
public sealed class UserAuthorizationVersionStore : IUserAuthorizationVersionStore
{
    private readonly ApplicationDbContext _db;

    public UserAuthorizationVersionStore(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var version = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (int?)u.AuthorizationVersion)
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null)
            throw new InvalidOperationException($"User '{userId}' was not found.");

        return version.Value;
    }

    public async Task IncrementAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            throw new InvalidOperationException($"User '{userId}' was not found.");

        user.IncrementAuthorizationVersion();
    }
}
