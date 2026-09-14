using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Hierarchy;

namespace Permixa.Application.Authorization.Hierarchy;

/// <summary>
/// Hierarchy policy over Identity-backed role levels.
/// Self-management of administrative hierarchy operations is denied.
/// Users without an effective level cannot perform hierarchy-gated operations.
/// Targets without a level are manageable by any actor who has a level
/// (roleless users have no hierarchical authority).
/// </summary>
public sealed class AuthorizationHierarchyService : IAuthorizationHierarchyService
{
    private readonly IRoleHierarchyReader _roleHierarchyReader;

    public AuthorizationHierarchyService(IRoleHierarchyReader roleHierarchyReader)
    {
        _roleHierarchyReader = roleHierarchyReader;
    }

    public async Task<bool> CanManageUserAsync(
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty || targetUserId == Guid.Empty)
            return false;

        if (actorUserId == targetUserId)
            return false;

        var actorLevel = await _roleHierarchyReader.GetEffectiveUserLevelAsync(actorUserId, cancellationToken);
        if (!RoleHierarchyRules.HasAuthority(actorLevel))
            return false;

        var targetLevel = await _roleHierarchyReader.GetEffectiveUserLevelAsync(targetUserId, cancellationToken);
        if (!targetLevel.HasValue)
            return true;

        return RoleHierarchyRules.CanManage(actorLevel!.Value, targetLevel.Value);
    }

    public async Task<bool> CanManageRoleAsync(
        Guid actorUserId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        return await CanControlRoleAsync(actorUserId, roleId, cancellationToken);
    }

    public async Task<bool> CanAssignRoleAsync(
        Guid actorUserId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        return await CanControlRoleAsync(actorUserId, roleId, cancellationToken);
    }

    public async Task<bool> CanCreateOrChangeRoleToLevelAsync(
        Guid actorUserId,
        int targetRoleLevel,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
            return false;

        var actorLevel = await _roleHierarchyReader.GetEffectiveUserLevelAsync(actorUserId, cancellationToken);
        if (!RoleHierarchyRules.HasAuthority(actorLevel))
            return false;

        return RoleHierarchyRules.CanControlRoleLevel(actorLevel!.Value, targetRoleLevel);
    }

    private async Task<bool> CanControlRoleAsync(
        Guid actorUserId,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || roleId == Guid.Empty)
            return false;

        var actorLevel = await _roleHierarchyReader.GetEffectiveUserLevelAsync(actorUserId, cancellationToken);
        if (!RoleHierarchyRules.HasAuthority(actorLevel))
            return false;

        var roleLevel = await _roleHierarchyReader.GetRoleLevelAsync(roleId, cancellationToken);
        if (!roleLevel.HasValue)
            return false;

        return RoleHierarchyRules.CanControlRoleLevel(actorLevel!.Value, roleLevel.Value);
    }
}
