using Permixa.Application.Authorization.Models;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Authorization.Abstractions;

/// <summary>
/// Application orchestration for effective permissions and authorization snapshots.
/// Distinct from Domain <c>PermissionAuthorizationResolver</c>, which only applies precedence.
/// </summary>
public interface IEffectivePermissionService
{
    Task<Result<AuthorizationSnapshot>> GetAuthorizationSnapshotAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> HasPermissionAsync(
        Guid userId,
        string permissionName,
        CancellationToken cancellationToken = default);
}
