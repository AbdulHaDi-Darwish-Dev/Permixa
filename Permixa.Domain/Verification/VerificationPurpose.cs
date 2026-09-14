namespace Permixa.Domain.Verification;

/// <summary>
/// Purpose of a verification challenge.
/// </summary>
public enum VerificationPurpose
{
    EmailConfirmation = 1,
    PhoneConfirmation = 2,
    PasswordReset = 3,
    TwoFactorAuthentication = 4,
    SensitiveOperation = 5,
    EmailChange = 6
}
