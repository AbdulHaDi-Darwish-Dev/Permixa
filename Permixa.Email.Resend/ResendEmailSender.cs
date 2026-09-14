using Permixa.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Resend;
using System.Net.Mail;

namespace Permixa.Email.Resend;

/// <summary>
/// Resend-backed <see cref="IEmailSender"/>. Sole component aware of IResend / Resend message types.
/// Uses the provider-neutral <see cref="EmailOutgoingMessage.From"/> value set by the delivery pipeline or host.
/// </summary>
public sealed class ResendEmailSender : IEmailSender
{
    private readonly IResend _resend;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(
        IResend resend,
        ILogger<ResendEmailSender> logger)
    {
        _resend = resend;
        _logger = logger;
    }

    public async Task SendAsync(
        EmailOutgoingMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        MailAddress fromAddress;
        try
        {
            fromAddress = new MailAddress(message.From);
        }
        catch (FormatException ex)
        {
            throw new EmailDeliveryException("Email delivery failed.", ex);
        }

        var email = new EmailMessage
        {
            From = new EmailAddress
            {
                Email = fromAddress.Address,
                DisplayName = fromAddress.DisplayName
            },
            To = message.To,
            Subject = message.Subject,
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody
        };

        try
        {
            var response = await _resend.EmailSendAsync(
                message.IdempotencyKey,
                email,
                cancellationToken);

            if (!response.Success)
            {
                Exception inner = response.Exception is not null
                    ? response.Exception
                    : new InvalidOperationException("Resend returned an unsuccessful response.");
                throw new EmailDeliveryException("Email delivery failed.", inner);
            }

            _logger.LogInformation(
                "Email delivered successfully. ProviderMessageId={ProviderMessageId}",
                response.Content);
        }
        catch (EmailDeliveryException)
        {
            throw;
        }
        catch (ResendException ex)
        {
            _logger.LogWarning(
                ex,
                "Email delivery failed with provider error. ErrorType={ErrorType} StatusCode={StatusCode}",
                ex.ErrorType,
                ex.StatusCode);
            throw new EmailDeliveryException("Email delivery failed.", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email delivery failed with an unexpected error.");
            throw new EmailDeliveryException("Email delivery failed.", ex);
        }
    }
}
