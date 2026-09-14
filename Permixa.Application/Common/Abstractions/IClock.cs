namespace Permixa.Application.Common.Abstractions;

/// <summary>
/// UTC clock abstraction for deterministic security and expiration logic.
/// Matches Domain entities that use <see cref="DateTime"/> UTC timestamps.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
