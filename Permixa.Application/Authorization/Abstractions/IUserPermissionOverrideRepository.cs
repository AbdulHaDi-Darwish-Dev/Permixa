using Permixa.Domain.Authorization;

namespace Permixa.Application.Authorization.Abstractions;

public interface IUserPermissionOverrideRepository
{
    Task<UserPermissionOverride?> GetAsync(
        Guid userId,
        Guid permissionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<UserPermissionOverride>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        UserPermissionOverride permissionOverride,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        Guid userId,
        Guid permissionId,
        CancellationToken cancellationToken = default);
}
