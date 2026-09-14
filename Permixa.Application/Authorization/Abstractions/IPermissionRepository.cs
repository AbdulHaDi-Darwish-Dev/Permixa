using Permixa.Domain.Authorization;

namespace Permixa.Application.Authorization.Abstractions;

public interface IPermissionRepository
{
    Task<Permission?> GetByIdAsync(Guid permissionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads permissions for the given ids in a single set-based query.
    /// Missing ids are omitted from the result.
    /// </summary>
    Task<IReadOnlyCollection<Permission>> GetByIdsAsync(
        IReadOnlyCollection<Guid> permissionIds,
        CancellationToken cancellationToken = default);

    Task<Permission?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Permission>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Permission>> GetByRoleIdsAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default);

    Task AddAsync(Permission permission, CancellationToken cancellationToken = default);
}
