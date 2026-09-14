using Microsoft.Extensions.Options;

namespace Permixa.Infrastructure.Email;

/// <summary>
/// Default link builder using configured absolute URL templates with {challengeId} and {token}.
/// </summary>
public sealed class ConfiguredVerificationLinkBuilder : IVerificationLinkBuilder
{
    private readonly PermixaEmailDeliveryOptions _options;

    public ConfiguredVerificationLinkBuilder(IOptions<PermixaEmailDeliveryOptions> options)
    {
        _options = options.Value;
    }

    public string BuildEmailConfirmationLink(Guid challengeId, string token) =>
        ApplyTemplate(_options.EmailConfirmationUrlTemplate, challengeId, token);

    public string BuildPasswordResetLink(Guid challengeId, string token) =>
        ApplyTemplate(_options.PasswordResetUrlTemplate, challengeId, token);

    public static string ApplyTemplate(string template, Guid challengeId, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return template
            .Replace("{challengeId}", challengeId.ToString("D"), StringComparison.Ordinal)
            .Replace("{token}", Uri.EscapeDataString(token), StringComparison.Ordinal);
    }
}
