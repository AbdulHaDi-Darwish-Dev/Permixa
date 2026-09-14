using Permixa.Application.Common.Abstractions;
using Permixa.Application.Identity.Abstractions;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Identity;

public sealed class IdentityUserWriter : IIdentityUserWriter
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public IdentityUserWriter(
        UserManager<ApplicationUser> users,
        ApplicationDbContext db,
        IClock clock)
    {
        _users = users;
        _db = db;
        _clock = clock;
    }

    public async Task<IdentityLockMutation> SetLockoutAsync(
        Guid userId,
        DateTime lockedUntilUtc,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
            return IdentityLockMutation.NotFound;

        var until = new DateTimeOffset(DateTime.SpecifyKind(lockedUntilUtc, DateTimeKind.Utc));
        if (user.LockoutEnabled
            && user.LockoutEnd is DateTimeOffset current
            && current >= until)
        {
            return IdentityLockMutation.Unchanged;
        }

        if (!user.LockoutEnabled)
        {
            var enabled = await _users.SetLockoutEnabledAsync(user, true);
            if (!enabled.Succeeded)
            {
                throw new InvalidOperationException(
                    "Failed to enable lockout: " + Format(enabled));
            }
        }

        var setEnd = await _users.SetLockoutEndDateAsync(user, until);
        if (!setEnd.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to set lockout end: " + Format(setEnd));
        }

        return IdentityLockMutation.Updated;
    }

    public async Task<IdentityLockMutation> UnlockAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
            return IdentityLockMutation.NotFound;

        var now = new DateTimeOffset(DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc));
        var currentlyLocked = user.LockoutEnabled
            && user.LockoutEnd is DateTimeOffset end
            && end > now;

        if (!currentlyLocked && user.AccessFailedCount == 0)
            return IdentityLockMutation.Unchanged;

        var cleared = await _users.SetLockoutEndDateAsync(user, null);
        if (!cleared.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to clear lockout: " + Format(cleared));
        }

        var reset = await _users.ResetAccessFailedCountAsync(user);
        if (!reset.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to reset access failed count: " + Format(reset));
        }

        return IdentityLockMutation.Updated;
    }

    public async Task<bool> SetDisabledAsync(
        Guid userId,
        bool isDisabled,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            throw new InvalidOperationException($"User '{userId}' was not found.");

        if (user.IsDisabled == isDisabled)
            return false;

        user.SetDisabled(isDisabled);
        return true;
    }

    public async Task SetPendingEmailAsync(
        Guid userId,
        string? pendingEmail,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            throw new InvalidOperationException($"User '{userId}' was not found.");

        user.SetPendingEmail(pendingEmail);
    }

    private static string Format(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => e.Description));
}
