using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Users.Models;

internal static class UserMapping
{
    public static IamUserDto ToDto(IdentityUserIamRecord user) =>
        new(
            user.Id,
            user.UserName,
            user.Email,
            user.EmailConfirmed,
            user.IsDisabled,
            user.IsLocked,
            user.LockoutEndUtc,
            user.EffectiveRoleLevel,
            user.TwoFactorEnabled,
            user.PendingEmail);
}
