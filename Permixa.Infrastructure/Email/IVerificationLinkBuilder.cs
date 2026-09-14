namespace Permixa.Infrastructure.Email;

/// <summary>
/// Builds absolute verification URLs for email bodies. Hosts may replace the default implementation.
/// </summary>
public interface IVerificationLinkBuilder
{
    string BuildEmailConfirmationLink(Guid challengeId, string token);

    string BuildPasswordResetLink(Guid challengeId, string token);
}
