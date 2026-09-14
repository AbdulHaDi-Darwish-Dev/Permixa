using Permixa.Application.Common.Results;

namespace Permixa.Application.Authentication;

public static class MfaErrors
{
    public static readonly Error ChallengeInvalid =
        Error.Unauthorized(
            "Authentication.MfaChallengeInvalid",
            "The MFA challenge is invalid.");

    public static readonly Error AttemptsExceeded =
        Error.Unauthorized(
            "Authentication.MfaAttemptsExceeded",
            "The MFA challenge has no remaining attempts.");

    public static readonly Error CodeInvalid =
        Error.Validation(
            "Authentication.MfaCodeInvalid",
            "The MFA code is invalid.");

    public static readonly Error AlreadyEnabled =
        Error.Conflict(
            "Authentication.MfaAlreadyEnabled",
            "Multi-factor authentication is already enabled.");

    public static readonly Error NotEnabled =
        Error.Conflict(
            "Authentication.MfaNotEnabled",
            "Multi-factor authentication is not enabled.");

    public static readonly Error AuthenticatorNotConfigured =
        Error.Validation(
            "Authentication.AuthenticatorNotConfigured",
            "An authenticator key has not been configured.");
}
