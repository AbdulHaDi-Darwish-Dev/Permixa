namespace Permixa.Application.Verification.Abstractions;

/// <summary>
/// Identity email-confirmation side effects after cryptographic proof succeeds.
/// </summary>
public interface IIdentityEmailConfirmation
{
    /// <summary>
    /// Validates an Identity email-confirmation URL token and marks the email confirmed.
    /// </summary>
    Task<bool> ConfirmEmailWithTokenAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the email confirmed after OTP validation has already succeeded.
    /// </summary>
    Task MarkEmailConfirmedAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
