namespace Permixa.Domain.Common;

/// <summary>
/// Thrown when a domain invariant would be violated.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}
