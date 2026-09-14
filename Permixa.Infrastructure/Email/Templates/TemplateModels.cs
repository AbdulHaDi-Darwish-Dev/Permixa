namespace Permixa.Infrastructure.Email.Templates;

public sealed class EmailConfirmationOtpTemplateModel
{
    public required string ApplicationName { get; init; }

    public string? CompanyName { get; init; }

    public string? LogoUrl { get; init; }

    public string? SupportEmail { get; init; }

    public required string VerificationCode { get; init; }

    public required int ExpirationMinutes { get; init; }
}

public sealed class EmailConfirmationLinkTemplateModel
{
    public required string ApplicationName { get; init; }

    public string? CompanyName { get; init; }

    public string? LogoUrl { get; init; }

    public string? SupportEmail { get; init; }

    public required string VerificationUrl { get; init; }

    public required int ExpirationMinutes { get; init; }
}

public sealed class PasswordResetTemplateModel
{
    public required string ApplicationName { get; init; }

    public string? CompanyName { get; init; }

    public string? LogoUrl { get; init; }

    public string? SupportEmail { get; init; }

    public required string ResetUrl { get; init; }

    public required int ExpirationMinutes { get; init; }
}

public sealed record RenderedEmail(
    string Subject,
    string HtmlBody,
    string TextBody);
