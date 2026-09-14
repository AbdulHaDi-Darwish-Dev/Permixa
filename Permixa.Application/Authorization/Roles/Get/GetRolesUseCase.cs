using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Roles.Models;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Roles.Get;

public sealed class GetRolesUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IRoleHierarchyReader _hierarchyReader;
    private readonly IIdentityRoleReader _roleReader;

    public GetRolesUseCase(
        IEffectivePermissionService effectivePermissions,
        IRoleHierarchyReader hierarchyReader,
        IIdentityRoleReader roleReader)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchyReader = hierarchyReader;
        _roleReader = roleReader;
    }

    public async Task<Result<IReadOnlyList<RoleDto>>> ExecuteAsync(
        GetRolesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var permissionCheck = await _effectivePermissions.HasPermissionAsync(
            query.ActorUserId,
            IamPermissions.Roles.Read,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure<IReadOnlyList<RoleDto>>(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure<IReadOnlyList<RoleDto>>(AuthorizationErrors.MissingManagePermission);

        var actorLevel = await _hierarchyReader.GetEffectiveUserLevelAsync(query.ActorUserId, cancellationToken);
        if (actorLevel is not int actorAuthority)
            return Result.Success<IReadOnlyList<RoleDto>>([]);
        var roles = await _roleReader.GetAllAsync(cancellationToken);
        var visible = roles
            .Where(r => RoleHierarchyRules.CanControlRoleLevel(actorAuthority, r.RoleLevel))
            .OrderBy(r => r.RoleLevel)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .Select(RoleMapping.ToDto)
            .ToList();

        return Result.Success<IReadOnlyList<RoleDto>>(visible);
    }
}
