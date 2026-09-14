using Permixa.Application.Common.Results;

namespace Permixa.Application.Authentication;

/// <summary>
/// Authentication-specific expected failures.
/// </summary>
public static class AuthenticationErrors
{
    public static readonly Error InvalidCredentials =
        Error.Unauthorized(
            "Authentication.InvalidCredentials",
            "The provided credentials are invalid.");

    public static readonly Error EmailAlreadyExists =
        Error.Conflict(
            "Authentication.EmailAlreadyExists",
            "An account with this email already exists.");

    public static readonly Error UserNameAlreadyExists =
        Error.Conflict(
            "Authentication.UserNameAlreadyExists",
            "An account with this user name already exists.");

    public static readonly Error InvalidPassword =
        Error.Validation(
            "Authentication.InvalidPassword",
            "The password does not meet the required policy.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized(
            "Authentication.InvalidRefreshToken",
            "The refresh token is invalid.");

    public static readonly Error RefreshTokenExpired =
        Error.Unauthorized(
            "Authentication.RefreshTokenExpired",
            "The refresh token has expired.");

    public static readonly Error RefreshTokenRevoked =
        Error.Unauthorized(
            "Authentication.RefreshTokenRevoked",
            "The refresh token has been revoked.");

    public static readonly Error RefreshTokenReuseDetected =
        Error.Unauthorized(
            "Authentication.RefreshTokenReuseDetected",
            "Refresh token reuse was detected. The token family has been revoked.");

    public static readonly Error LockedOut =
        Error.Unauthorized(
            "Authentication.LockedOut",
            "The account is temporarily locked due to failed sign-in attempts.");

    public static readonly Error EmailNotConfirmed =
        Error.Unauthorized(
            "Authentication.EmailNotConfirmed",
            "The email address has not been confirmed.");

    public static readonly Error CurrentPasswordInvalid =
        Error.Validation(
            "Authentication.CurrentPasswordInvalid",
            "The current password is incorrect.");

    public static readonly Error EmailUnchanged =
        Error.Validation(
            "Authentication.EmailUnchanged",
            "The new email is the same as the current email.");

    public static readonly Error InvalidEmail =
        Error.Validation(
            "Authentication.InvalidEmail",
            "Email is required.");
}
