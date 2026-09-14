using Permixa.Domain.Authorization;

namespace Permixa.Application.Authorization.Abstractions;

public interface IAuthorizationStateRepository
{
    /// <summary>
    /// Returns the shared global authorization state, or null if not yet seeded.
    /// </summary>
    Task<AuthorizationState?> GetAsync(CancellationToken cancellationToken = default);

    Task AddAsync(AuthorizationState state, CancellationToken cancellationToken = default);
}
