namespace Permixa.Infrastructure.Email;

/// <summary>
/// Provider-neutral email delivery / branding / verification URL configuration.
/// Transport credentials (e.g. Resend ApiKey) belong in the provider package options.
/// </summary>
public sealed class PermixaEmailDeliveryOptions
{
    public string FromEmail { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;

    public PermixaEmailBrandingOptions Branding { get; set; } = new();

    /// <summary>
    /// Absolute URL template containing {challengeId} and {token} placeholders.
    /// </summary>
    public string EmailConfirmationUrlTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Absolute URL template containing {challengeId} and {token} placeholders.
    /// </summary>
    public string PasswordResetUrlTemplate { get; set; } = string.Empty;
}

public sealed class PermixaEmailBrandingOptions
{
    public string ApplicationName { get; set; } = string.Empty;

    public string? CompanyName { get; set; }

    public string? LogoUrl { get; set; }

    public string? SupportEmail { get; set; }
}
