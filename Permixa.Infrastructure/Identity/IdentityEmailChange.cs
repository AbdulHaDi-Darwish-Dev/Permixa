using Permixa.Application.Verification.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Permixa.Infrastructure.Identity;

public sealed class IdentityEmailChange : IIdentityEmailChange
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentityEmailChange(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<IdentityEmailChangeResult> ChangeEmailAsync(
        Guid userId,
        string newEmail,
        string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return IdentityEmailChangeResult.NotFound();

        var result = await _userManager.ChangeEmailAsync(user, newEmail, token);
        if (result.Succeeded)
            return IdentityEmailChangeResult.Success();

        if (result.Errors.Any(e =>
                string.Equals(e.Code, "InvalidToken", StringComparison.OrdinalIgnoreCase)))
        {
            return IdentityEmailChangeResult.FailedInvalidToken();
        }

        if (result.Errors.Any(e =>
                string.Equals(e.Code, "DuplicateEmail", StringComparison.OrdinalIgnoreCase)))
        {
            return IdentityEmailChangeResult.FailedDuplicateEmail();
        }

        return IdentityEmailChangeResult.Failed();
    }
}
