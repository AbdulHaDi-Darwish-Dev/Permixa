namespace Permixa.Application.Authorization.Abstractions;

/// <summary>
/// Application hierarchy policy checks (orchestration over role levels).
/// Implementation will use <see cref="IRoleHierarchyReader"/> later.
/// </summary>
public interface IAuthorizationHierarchyService
{
    Task<bool> CanManageUserAsync(
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default);

    Task<bool> CanManageRoleAsync(
        Guid actorUserId,
        Guid roleId,
        CancellationToken cancellationToken = default);

    Task<bool> CanAssignRoleAsync(
        Guid actorUserId,
        Guid roleId,
        CancellationToken cancellationToken = default);

    Task<bool> CanCreateOrChangeRoleToLevelAsync(
        Guid actorUserId,
        int targetRoleLevel,
        CancellationToken cancellationToken = default);
}
