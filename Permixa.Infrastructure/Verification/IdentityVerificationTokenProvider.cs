using Permixa.Application.Verification.Abstractions;
using Permixa.Domain.Verification;
using Permixa.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Permixa.Infrastructure.Verification;

/// <summary>
/// Adapts ASP.NET Core Identity token providers. No Permixa cryptography; no SecretHash persistence.
/// </summary>
public sealed class IdentityVerificationTokenProvider : IVerificationTokenProvider
{
    /// <summary>
    /// Purpose string for Email OTP via Identity's Email token provider (distinct from DataProtector email confirmation).
    /// </summary>
    public const string EmailOtpProviderPurpose = "EmailConfirmation";

    private readonly UserManager<ApplicationUser> _userManager;

    public IdentityVerificationTokenProvider(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<string> GenerateAsync(
        Guid userId,
        VerificationPurpose purpose,
        VerificationMethod method,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireUserAsync(userId);

        return (purpose, method) switch
        {
            (VerificationPurpose.EmailConfirmation, VerificationMethod.UrlToken) =>
                await _userManager.GenerateEmailConfirmationTokenAsync(user),

            (VerificationPurpose.EmailConfirmation, VerificationMethod.Otp) =>
                await _userManager.GenerateUserTokenAsync(
                    user,
                    TokenOptions.DefaultEmailProvider,
                    EmailOtpProviderPurpose),

            (VerificationPurpose.PasswordReset, VerificationMethod.UrlToken) =>
                await _userManager.GeneratePasswordResetTokenAsync(user),

            (VerificationPurpose.EmailChange, VerificationMethod.UrlToken) =>
                await GenerateEmailChangeTokenAsync(user),

            _ => throw new InvalidOperationException(
                $"Verification token generation is not supported for purpose '{purpose}' with method '{method}'.")
        };
    }

    public async Task<bool> ValidateAsync(
        Guid userId,
        VerificationPurpose purpose,
        VerificationMethod method,
        string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(token))
            return false;

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;

        return (purpose, method) switch
        {
            (VerificationPurpose.EmailConfirmation, VerificationMethod.Otp) =>
                await _userManager.VerifyUserTokenAsync(
                    user,
                    TokenOptions.DefaultEmailProvider,
                    EmailOtpProviderPurpose,
                    token),

            (VerificationPurpose.EmailConfirmation, VerificationMethod.UrlToken) =>
                await _userManager.VerifyUserTokenAsync(
                    user,
                    _userManager.Options.Tokens.EmailConfirmationTokenProvider,
                    UserManager<ApplicationUser>.ConfirmEmailTokenPurpose,
                    token),

            (VerificationPurpose.PasswordReset, VerificationMethod.UrlToken) =>
                await _userManager.VerifyUserTokenAsync(
                    user,
                    _userManager.Options.Tokens.PasswordResetTokenProvider,
                    UserManager<ApplicationUser>.ResetPasswordTokenPurpose,
                    token),

            (VerificationPurpose.EmailChange, VerificationMethod.UrlToken) =>
                await VerifyEmailChangeTokenAsync(user, token),

            _ => false
        };
    }

    private async Task<string> GenerateEmailChangeTokenAsync(ApplicationUser user)
    {
        if (string.IsNullOrWhiteSpace(user.PendingEmail))
        {
            throw new InvalidOperationException(
                "Email change token generation requires a pending email.");
        }

        return await _userManager.GenerateChangeEmailTokenAsync(user, user.PendingEmail);
    }

    private async Task<bool> VerifyEmailChangeTokenAsync(ApplicationUser user, string token)
    {
        if (string.IsNullOrWhiteSpace(user.PendingEmail))
            return false;

        return await _userManager.VerifyUserTokenAsync(
            user,
            _userManager.Options.Tokens.ChangeEmailTokenProvider,
            UserManager<ApplicationUser>.GetChangeEmailTokenPurpose(user.PendingEmail),
            token);
    }

    private async Task<ApplicationUser> RequireUserAsync(Guid userId)
    {
        return await _userManager.FindByIdAsync(userId.ToString())
               ?? throw new InvalidOperationException($"User '{userId}' was not found for token generation.");
    }
}
