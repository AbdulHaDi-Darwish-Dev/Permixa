namespace Permixa.Email.Resend;

/// <summary>
/// Resend-specific transport options. Provider-neutral From/branding/URL templates live in
/// <c>PermixaEmailDeliveryOptions</c> via <c>AddPermixaEmailDelivery</c>.
/// </summary>
public sealed class PermixaResendEmailOptions
{
    /// <summary>
    /// Resend API token. Required when registering the Resend email transport.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
