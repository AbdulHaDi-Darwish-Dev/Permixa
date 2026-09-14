using Permixa.Application.Authentication.Abstractions;
using Permixa.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Permixa.Infrastructure.Identity;

public sealed class IdentityUserCreator : IIdentityUserCreator
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentityUserCreator(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<IdentityUserCreationResult> CreateAsync(
        string userName,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (await _userManager.FindByEmailAsync(email) is not null)
            return IdentityUserCreationResult.Failed(IdentityUserCreationFailure.DuplicateEmail);

        if (await _userManager.FindByNameAsync(userName) is not null)
            return IdentityUserCreationResult.Failed(IdentityUserCreationFailure.DuplicateUserName);

        var user = new ApplicationUser(userName) { Email = email };
        var result = await _userManager.CreateAsync(user, password);

        if (result.Succeeded)
            return IdentityUserCreationResult.Success(user.Id, user.EmailConfirmed);

        if (result.Errors.Any(e => e.Code.Contains("Password", StringComparison.OrdinalIgnoreCase)))
            return IdentityUserCreationResult.Failed(IdentityUserCreationFailure.InvalidPassword);

        if (result.Errors.Any(e =>
                e.Code.Contains("DuplicateEmail", StringComparison.OrdinalIgnoreCase)
                || e.Code.Equals("DuplicateEmail", StringComparison.OrdinalIgnoreCase)))
            return IdentityUserCreationResult.Failed(IdentityUserCreationFailure.DuplicateEmail);

        if (result.Errors.Any(e =>
                e.Code.Contains("DuplicateUserName", StringComparison.OrdinalIgnoreCase)))
            return IdentityUserCreationResult.Failed(IdentityUserCreationFailure.DuplicateUserName);

        return IdentityUserCreationResult.Failed(IdentityUserCreationFailure.Failed);
    }
}
