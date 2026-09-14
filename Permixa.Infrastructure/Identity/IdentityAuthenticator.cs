using Permixa.Application.Authentication.Abstractions;
using Permixa.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Permixa.Infrastructure.Identity;

public sealed class IdentityAuthenticator : IIdentityAuthenticator
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentityAuthenticator(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<IdentityAuthenticationResult> AuthenticateAsync(
        string emailOrUserName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(emailOrUserName)
                   ?? await _userManager.FindByNameAsync(emailOrUserName);

        if (user is null)
            return IdentityAuthenticationResult.InvalidCredentials();

        if (user.IsDisabled)
            return IdentityAuthenticationResult.InvalidCredentials();

        if (await _userManager.IsLockedOutAsync(user))
            return IdentityAuthenticationResult.LockedOut();

        if (!await _userManager.CheckPasswordAsync(user, password))
        {
            await _userManager.AccessFailedAsync(user);
            // Do not surface lockout that was just triggered by this failure as a distinct
            // signal mid-attempt; the next authentication attempt will report LockedOut.
            return IdentityAuthenticationResult.InvalidCredentials();
        }

        await _userManager.ResetAccessFailedCountAsync(user);
        return IdentityAuthenticationResult.Success(user.Id, user.EmailConfirmed, user.TwoFactorEnabled);
    }
}
