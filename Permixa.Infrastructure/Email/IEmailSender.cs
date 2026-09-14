namespace Permixa.Infrastructure.Email;

/// <summary>
/// Provider-neutral outbound email message. Verification uses a single recipient.
/// </summary>
public sealed record EmailOutgoingMessage(
    string To,
    string From,
    string Subject,
    string HtmlBody,
    string TextBody,
    string IdempotencyKey);

/// <summary>
/// Provider-neutral email sender. Resend-specific types must not appear here.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(
        EmailOutgoingMessage message,
        CancellationToken cancellationToken = default);
}
