using Permixa.Application.Verification.Abstractions;
using Permixa.Application.Verification.Models;
using Permixa.Domain.Verification;
using Permixa.Infrastructure.Email.Templates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Permixa.Infrastructure.Email;

/// <summary>
/// Phase 7 email verification dispatcher. Supports only approved Email channel flows.
/// </summary>
public sealed class EmailVerificationDispatcher : IVerificationDispatcher
{
    public const string IdempotencyKeyPrefix = "permixa-verification/";

    private readonly IEmailTemplateRenderer _renderer;
    private readonly IVerificationLinkBuilder _links;
    private readonly IEmailSender _sender;
    private readonly PermixaEmailDeliveryOptions _options;
    private readonly ILogger<EmailVerificationDispatcher> _logger;

    public EmailVerificationDispatcher(
        IEmailTemplateRenderer renderer,
        IVerificationLinkBuilder links,
        IEmailSender sender,
        IOptions<PermixaEmailDeliveryOptions> options,
        ILogger<EmailVerificationDispatcher> logger)
    {
        _renderer = renderer;
        _links = links;
        _sender = sender;
        _options = options.Value;
        _logger = logger;
    }

    public static string CreateIdempotencyKey(Guid challengeId) =>
        IdempotencyKeyPrefix + challengeId.ToString("D");

    public async Task DispatchAsync(
        VerificationDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Channel != VerificationChannel.Email)
        {
            throw new EmailDeliveryException(
                $"Verification channel '{request.Channel}' is not supported by the email dispatcher.");
        }

        var branding = _options.Branding;
        var expirationMinutes = Math.Max(
            1,
            (int)Math.Ceiling((request.ExpiresAtUtc - DateTime.UtcNow).TotalMinutes));

        RenderedEmail rendered = (request.Purpose, request.Method) switch
        {
            (VerificationPurpose.EmailConfirmation, VerificationMethod.Otp) =>
                _renderer.RenderEmailConfirmationOtp(new EmailConfirmationOtpTemplateModel
                {
                    ApplicationName = branding.ApplicationName,
                    CompanyName = branding.CompanyName,
                    LogoUrl = branding.LogoUrl,
                    SupportEmail = branding.SupportEmail,
                    VerificationCode = request.RawVerificationValue,
                    ExpirationMinutes = expirationMinutes
                }),

            (VerificationPurpose.EmailConfirmation, VerificationMethod.UrlToken) =>
                _renderer.RenderEmailConfirmationLink(new EmailConfirmationLinkTemplateModel
                {
                    ApplicationName = branding.ApplicationName,
                    CompanyName = branding.CompanyName,
                    LogoUrl = branding.LogoUrl,
                    SupportEmail = branding.SupportEmail,
                    VerificationUrl = _links.BuildEmailConfirmationLink(
                        request.ChallengeId,
                        request.RawVerificationValue),
                    ExpirationMinutes = expirationMinutes
                }),

            (VerificationPurpose.PasswordReset, VerificationMethod.UrlToken) =>
                _renderer.RenderPasswordReset(new PasswordResetTemplateModel
                {
                    ApplicationName = branding.ApplicationName,
                    CompanyName = branding.CompanyName,
                    LogoUrl = branding.LogoUrl,
                    SupportEmail = branding.SupportEmail,
                    ResetUrl = _links.BuildPasswordResetLink(
                        request.ChallengeId,
                        request.RawVerificationValue),
                    ExpirationMinutes = expirationMinutes
                }),

            (VerificationPurpose.EmailChange, VerificationMethod.UrlToken) =>
                _renderer.RenderEmailConfirmationLink(new EmailConfirmationLinkTemplateModel
                {
                    ApplicationName = branding.ApplicationName,
                    CompanyName = branding.CompanyName,
                    LogoUrl = branding.LogoUrl,
                    SupportEmail = branding.SupportEmail,
                    VerificationUrl = _links.BuildEmailConfirmationLink(
                        request.ChallengeId,
                        request.RawVerificationValue),
                    ExpirationMinutes = expirationMinutes
                }),

            _ => throw new EmailDeliveryException(
                $"Unsupported verification combination: Purpose={request.Purpose}, Method={request.Method}, Channel={request.Channel}.")
        };

        var from = $"{_options.FromName} <{_options.FromEmail}>";
        var idempotencyKey = CreateIdempotencyKey(request.ChallengeId);

        _logger.LogInformation(
            "Dispatching verification email. ChallengeId={ChallengeId} Purpose={Purpose} Method={Method} Channel={Channel}",
            request.ChallengeId,
            request.Purpose,
            request.Method,
            request.Channel);

        await _sender.SendAsync(
            new EmailOutgoingMessage(
                request.Destination,
                from,
                rendered.Subject,
                rendered.HtmlBody,
                rendered.TextBody,
                idempotencyKey),
            cancellationToken);
    }
}
