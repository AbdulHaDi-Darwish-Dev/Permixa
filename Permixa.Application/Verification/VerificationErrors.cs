using Permixa.Application.Common.Results;

namespace Permixa.Application.Verification;

/// <summary>
/// Verification-specific expected failures.
/// </summary>
public static class VerificationErrors
{
    public static readonly Error ChallengeNotFound =
        Error.NotFound(
            "Verification.ChallengeNotFound",
            "The verification challenge was not found.");

    public static readonly Error Expired =
        Error.Validation(
            "Verification.Expired",
            "The verification challenge has expired.");

    public static readonly Error InvalidCode =
        Error.Validation(
            "Verification.InvalidCode",
            "The verification code is invalid.");

    public static readonly Error InvalidToken =
        Error.Validation(
            "Verification.InvalidToken",
            "The verification token is invalid.");

    public static readonly Error Invalidated =
        Error.Validation(
            "Verification.Invalidated",
            "The verification challenge has been invalidated.");

    public static readonly Error AlreadyConsumed =
        Error.Conflict(
            "Verification.AlreadyConsumed",
            "The verification challenge has already been consumed.");

    public static readonly Error TooManyAttempts =
        Error.Validation(
            "Verification.TooManyAttempts",
            "The maximum number of verification attempts has been reached.");

    public static readonly Error ResendTooSoon =
        Error.Conflict(
            "Verification.ResendTooSoon",
            "A verification challenge was requested too recently. Try again later.");

    public static readonly Error UnsupportedMethod =
        Error.Validation(
            "Verification.UnsupportedMethod",
            "The verification method is not supported for this purpose.");

    public static readonly Error DeliveryFailed =
        Error.Failure(
            "Verification.DeliveryFailed",
            "The verification value could not be delivered.");

    public static readonly Error RequestConflict =
        Error.Conflict(
            "Verification.RequestConflict",
            "Another verification challenge is already open for this destination.");

    public static readonly Error DestinationMismatch =
        Error.Validation(
            "Verification.DestinationMismatch",
            "The verification challenge destination no longer matches the user.");

    public static readonly Error UserNotFound =
        Error.NotFound(
            "Verification.UserNotFound",
            "The user was not found.");

    public static readonly Error AlreadyConfirmed =
        Error.Conflict(
            "Verification.AlreadyConfirmed",
            "The email address is already confirmed.");
}
