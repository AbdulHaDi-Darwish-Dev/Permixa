namespace Permixa.Application.Authentication.Abstractions;

/// <summary>
/// Password authentication against ASP.NET Core Identity without exposing Identity types.
/// </summary>
public interface IIdentityAuthenticator
{
    /// <summary>
    /// Authenticates by email or user name. Does not reveal which identifier matched.
    /// </summary>
    Task<IdentityAuthenticationResult> AuthenticateAsync(
        string emailOrUserName,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed class IdentityAuthenticationResult
{
    private IdentityAuthenticationResult(
        bool succeeded,
        Guid? userId,
        bool isLockedOut,
        bool emailConfirmed,
        bool twoFactorEnabled)
    {
        Succeeded = succeeded;
        UserId = userId;
        IsLockedOut = isLockedOut;
        EmailConfirmed = emailConfirmed;
        TwoFactorEnabled = twoFactorEnabled;
    }

    public bool Succeeded { get; }

    public Guid? UserId { get; }

    public bool IsLockedOut { get; }

    public bool EmailConfirmed { get; }

    public bool TwoFactorEnabled { get; }

    public static IdentityAuthenticationResult Success(
        Guid userId,
        bool emailConfirmed,
        bool twoFactorEnabled = false) =>
        new(true, userId, false, emailConfirmed, twoFactorEnabled);

    public static IdentityAuthenticationResult InvalidCredentials() =>
        new(false, null, false, false, false);

    public static IdentityAuthenticationResult LockedOut() =>
        new(false, null, true, false, false);
}
