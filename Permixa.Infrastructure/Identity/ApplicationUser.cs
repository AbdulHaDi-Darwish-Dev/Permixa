using Microsoft.AspNetCore.Identity;

namespace Permixa.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity user with framework authorization versioning.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public const int InitialAuthorizationVersion = 1;

    public ApplicationUser()
    {
        AuthorizationVersion = InitialAuthorizationVersion;
        IsDisabled = false;
    }

    public ApplicationUser(string userName)
        : base(userName)
    {
        AuthorizationVersion = InitialAuthorizationVersion;
        IsDisabled = false;
    }

    /// <summary>
    /// Administrative account shutdown. Distinct from Identity lockout.
    /// Disabled accounts cannot login or refresh. Existing access JWTs are not revoked.
    /// </summary>
    public bool IsDisabled { get; private set; }

    public void SetDisabled(bool isDisabled) => IsDisabled = isDisabled;

    /// <summary>
    /// Requested replacement email. The current <see cref="IdentityUser{TKey}.Email"/>
    /// remains active until the pending address is verified.
    /// </summary>
    public string? PendingEmail { get; private set; }

    public void SetPendingEmail(string? pendingEmail) => PendingEmail = pendingEmail;

    /// <summary>
    /// User-specific authorization cache invalidation token. Starts at 1.
    /// Treated as an EF concurrency token to prevent lost increments.
    /// </summary>
    public int AuthorizationVersion { get; private set; }

    public int IncrementAuthorizationVersion()
    {
        checked
        {
            AuthorizationVersion++;
        }

        return AuthorizationVersion;
    }
}
