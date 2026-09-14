using Permixa.Application.Authorization.Models;

namespace Permixa.Application.Authorization.Abstractions;

/// <summary>
/// Provider-independent cache for versioned effective authorization snapshots.
/// Key construction belongs to the Infrastructure implementation.
/// </summary>
public interface IPermissionCache
{
    Task<AuthorizationSnapshot?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        Guid userId,
        AuthorizationSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
