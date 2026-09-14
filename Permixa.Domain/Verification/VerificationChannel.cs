namespace Permixa.Domain.Verification;

/// <summary>
/// Delivery channel for a verification challenge.
/// Independent of the verification method.
/// </summary>
public enum VerificationChannel
{
    Email = 1,
    Sms = 2
}
