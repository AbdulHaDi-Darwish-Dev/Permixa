using Permixa.Application.Authentication.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Permixa.Infrastructure.Identity;

public sealed class IdentityPasswordChange : IIdentityPasswordChange
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentityPasswordChange(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<bool> CheckPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user is not null && await _userManager.CheckPasswordAsync(user, password);
    }

    public async Task<IdentityPasswordChangeResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return IdentityPasswordChangeResult.NotFound();

        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (result.Succeeded)
            return IdentityPasswordChangeResult.Success();

        if (result.Errors.Any(e =>
                string.Equals(e.Code, "PasswordMismatch", StringComparison.OrdinalIgnoreCase)))
        {
            return IdentityPasswordChangeResult.FailedCurrentPassword();
        }

        if (result.Errors.Any(e =>
                e.Code.Contains("Password", StringComparison.OrdinalIgnoreCase)))
        {
            return IdentityPasswordChangeResult.FailedNewPassword();
        }

        return IdentityPasswordChangeResult.Failed();
    }
}
