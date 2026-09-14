namespace Permixa.Infrastructure.Email;

/// <summary>
/// Provider-neutral delivery failure. Does not expose Resend types or API keys.
/// </summary>
public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException(string message)
        : base(message)
    {
    }

    public EmailDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
