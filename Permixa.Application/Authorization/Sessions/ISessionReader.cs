using Permixa.Application.Authorization.Sessions.Models;

namespace Permixa.Application.Authorization.Sessions;

/// <summary>
/// Read-only refresh-family session projections. No Session entity is persisted.
/// </summary>
public interface ISessionReader
{
    Task<IReadOnlyList<SessionFamilyRecord>> GetActiveFamiliesForUserAsync(
        Guid userId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}