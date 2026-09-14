using Permixa.Domain.Authorization;

namespace Permixa.Application.Authorization.Abstractions;

public interface IRolePermissionRepository
{
    Task<bool> ExistsAsync(
        Guid roleId,
        Guid permissionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> GetPermissionIdsByRoleIdsAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<RolePermission>> GetByRoleIdAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByRoleIdAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task AddAsync(RolePermission rolePermission, CancellationToken cancellationToken = default);

    Task RemoveAsync(
        Guid roleId,
        Guid permissionId,
        CancellationToken cancellationToken = default);
}
