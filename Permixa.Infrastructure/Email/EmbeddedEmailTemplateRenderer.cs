using System.Net;
using System.Reflection;
using System.Text;
using Permixa.Infrastructure.Email.Templates;

namespace Permixa.Infrastructure.Email;

/// <summary>
/// Embedded HTML + text templates with context-aware placeholder substitution.
/// </summary>
public sealed class EmbeddedEmailTemplateRenderer : IEmailTemplateRenderer
{
    private static readonly Assembly Assembly = typeof(EmbeddedEmailTemplateRenderer).Assembly;
    private const string ResourcePrefix = "Permixa.Infrastructure.Email.Templates.";

    public RenderedEmail RenderEmailConfirmationOtp(EmailConfirmationOtpTemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var html = Apply(
            Load("Verification.EmailConfirmationOtp.html"),
            BuildCommonHtml(model.ApplicationName, model.CompanyName, model.LogoUrl, model.SupportEmail),
            new Dictionary<string, string>
            {
                ["VerificationCode"] = Html(model.VerificationCode),
                ["ExpirationMinutes"] = Html(model.ExpirationMinutes.ToString())
            });

        var text = Apply(
            Load("Verification.EmailConfirmationOtp.txt"),
            BuildCommonText(model.ApplicationName, model.CompanyName, model.SupportEmail),
            new Dictionary<string, string>
            {
                ["VerificationCode"] = model.VerificationCode,
                ["ExpirationMinutes"] = model.ExpirationMinutes.ToString()
            });

        return new RenderedEmail(
            $"Your {model.ApplicationName} verification code",
            html,
            text);
    }

    public RenderedEmail RenderEmailConfirmationLink(EmailConfirmationLinkTemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var html = Apply(
            Load("Verification.EmailConfirmationLink.html"),
            BuildCommonHtml(model.ApplicationName, model.CompanyName, model.LogoUrl, model.SupportEmail),
            new Dictionary<string, string>
            {
                ["VerificationUrl"] = HtmlAttribute(model.VerificationUrl),
                ["ExpirationMinutes"] = Html(model.ExpirationMinutes.ToString())
            });

        var text = Apply(
            Load("Verification.EmailConfirmationLink.txt"),
            BuildCommonText(model.ApplicationName, model.CompanyName, model.SupportEmail),
            new Dictionary<string, string>
            {
                ["VerificationUrl"] = model.VerificationUrl,
                ["ExpirationMinutes"] = model.ExpirationMinutes.ToString()
            });

        return new RenderedEmail(
            $"Confirm your email for {model.ApplicationName}",
            html,
            text);
    }

    public RenderedEmail RenderPasswordReset(PasswordResetTemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var html = Apply(
            Load("Password.PasswordReset.html"),
            BuildCommonHtml(model.ApplicationName, model.CompanyName, model.LogoUrl, model.SupportEmail),
            new Dictionary<string, string>
            {
                ["ResetUrl"] = HtmlAttribute(model.ResetUrl),
                ["ExpirationMinutes"] = Html(model.ExpirationMinutes.ToString())
            });

        var text = Apply(
            Load("Password.PasswordReset.txt"),
            BuildCommonText(model.ApplicationName, model.CompanyName, model.SupportEmail),
            new Dictionary<string, string>
            {
                ["ResetUrl"] = model.ResetUrl,
                ["ExpirationMinutes"] = model.ExpirationMinutes.ToString()
            });

        return new RenderedEmail(
            $"Reset your {model.ApplicationName} password",
            html,
            text);
    }

    private static Dictionary<string, string> BuildCommonHtml(
        string applicationName,
        string? companyName,
        string? logoUrl,
        string? supportEmail) =>
        new()
        {
            ["ApplicationName"] = Html(applicationName),
            ["CompanyName"] = Html(companyName ?? string.Empty),
            ["SupportEmail"] = Html(supportEmail ?? string.Empty),
            ["LogoBlock"] = BuildLogoHtml(logoUrl),
            ["CompanyBlock"] = string.IsNullOrWhiteSpace(companyName)
                ? string.Empty
                : $"<p style=\"margin:8px 0 0;color:#555;font-size:13px;\">{Html(companyName)}</p>",
            ["SupportBlock"] = string.IsNullOrWhiteSpace(supportEmail)
                ? string.Empty
                : $"<p style=\"margin:16px 0 0;color:#555;font-size:13px;\">Need help? Contact {Html(supportEmail)}</p>"
        };

    private static Dictionary<string, string> BuildCommonText(
        string applicationName,
        string? companyName,
        string? supportEmail) =>
        new()
        {
            ["ApplicationName"] = applicationName,
            ["CompanyName"] = companyName ?? string.Empty,
            ["SupportEmail"] = supportEmail ?? string.Empty,
            ["CompanyLine"] = string.IsNullOrWhiteSpace(companyName)
                ? string.Empty
                : companyName + Environment.NewLine,
            ["SupportLine"] = string.IsNullOrWhiteSpace(supportEmail)
                ? string.Empty
                : "Need help? Contact " + supportEmail + Environment.NewLine
        };

    private static string BuildLogoHtml(string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl))
            return string.Empty;

        return $"<img src=\"{HtmlAttribute(logoUrl)}\" alt=\"\" width=\"120\" style=\"display:block;border:0;outline:none;text-decoration:none;margin:0 0 16px;\" />";
    }

    private static string Apply(
        string template,
        Dictionary<string, string> common,
        Dictionary<string, string> specific)
    {
        var result = template;
        foreach (var pair in common.Concat(specific))
            result = result.Replace("{{" + pair.Key + "}}", pair.Value, StringComparison.Ordinal);
        return result;
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private static string HtmlAttribute(string value) => WebUtility.HtmlEncode(value);

    private static string Load(string relativeName)
    {
        var resourceName = ResourcePrefix + relativeName;
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded email template '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
