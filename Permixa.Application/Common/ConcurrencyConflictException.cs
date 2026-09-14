namespace Permixa.Application.Common;

/// <summary>
/// Raised when a persistence concurrency token conflict prevents saving.
/// Infrastructure maps provider-specific concurrency failures to this type.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
