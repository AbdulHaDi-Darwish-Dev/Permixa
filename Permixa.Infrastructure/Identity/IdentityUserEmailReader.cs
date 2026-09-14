using Permixa.Application.Verification.Abstractions;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Identity;

public sealed class IdentityUserEmailReader : IIdentityUserEmailReader
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;

    public IdentityUserEmailReader(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public async Task<Guid?> FindUserIdByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByEmailAsync(email);
        return user?.Id;
    }

    public async Task<string?> GetEmailAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user?.Email;
    }

    public async Task<bool> IsEmailConfirmedAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user is not null && user.EmailConfirmed;
    }

    public async Task<IdentityEmailProfile?> GetEmailProfileAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new IdentityEmailProfile(
                u.Id,
                u.Email,
                u.NormalizedEmail,
                u.PendingEmail,
                u.EmailConfirmed))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> IsEmailClaimedByAnotherUserAsync(
        Guid userId,
        string email,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = _userManager.NormalizeEmail(email.Trim());
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return await _db.Users.AsNoTracking()
            .AnyAsync(
                u => u.Id != userId
                     && (u.NormalizedEmail == normalized
                         || (u.PendingEmail != null && u.PendingEmail.ToUpper() == normalized)),
                cancellationToken);
    }

    public async Task<bool> IsNormalizedEmailTakenByAnotherUserAsync(
        Guid userId,
        string email,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = _userManager.NormalizeEmail(email.Trim());
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return await _db.Users.AsNoTracking()
            .AnyAsync(
                u => u.Id != userId && u.NormalizedEmail == normalized,
                cancellationToken);
    }
}
