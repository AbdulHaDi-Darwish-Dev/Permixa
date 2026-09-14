using Permixa.Application.Verification.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Permixa.Infrastructure.Identity;

public sealed class IdentityPasswordReset : IIdentityPasswordReset
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentityPasswordReset(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<IdentityPasswordResetResult> ResetPasswordAsync(
        Guid userId,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return IdentityPasswordResetResult.FailedInvalidToken();

        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (result.Succeeded)
            return IdentityPasswordResetResult.Success();

        if (result.Errors.Any(e =>
                string.Equals(e.Code, "InvalidToken", StringComparison.OrdinalIgnoreCase)))
        {
            return IdentityPasswordResetResult.FailedInvalidToken();
        }

        if (result.Errors.Any(e =>
                e.Code.Contains("Password", StringComparison.OrdinalIgnoreCase)))
        {
            return IdentityPasswordResetResult.FailedInvalidPassword();
        }

        return IdentityPasswordResetResult.Failed();
    }
}
