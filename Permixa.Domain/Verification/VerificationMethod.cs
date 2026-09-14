namespace Permixa.Domain.Verification;

/// <summary>
/// How the verification secret is presented to the user.
/// Independent of the delivery channel.
/// </summary>
public enum VerificationMethod
{
    Otp = 1,
    UrlToken = 2
}
