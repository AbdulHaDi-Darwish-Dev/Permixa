using Permixa.Application.Verification.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Permixa.Infrastructure.Identity;

public sealed class IdentityEmailConfirmation : IIdentityEmailConfirmation
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentityEmailConfirmation(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<bool> ConfirmEmailWithTokenAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;

        var result = await _userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded;
    }

    public async Task MarkEmailConfirmedAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString())
                   ?? throw new InvalidOperationException("User was not found for email confirmation.");

        if (user.EmailConfirmed)
            return;

        user.EmailConfirmed = true;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to mark email as confirmed: "
                + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
