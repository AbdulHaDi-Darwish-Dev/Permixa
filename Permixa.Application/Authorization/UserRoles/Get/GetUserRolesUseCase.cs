using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Roles.Models;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.UserRoles.Get;

public sealed class GetUserRolesUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IIdentityUserReader _userReader;
    private readonly IIdentityUserRoleReader _userRoles;

    public GetUserRolesUseCase(
        IEffectivePermissionService effectivePermissions,
        IAuthorizationHierarchyService hierarchy,
        IIdentityUserReader userReader,
        IIdentityUserRoleReader userRoles)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchy = hierarchy;
        _userReader = userReader;
        _userRoles = userRoles;
    }

    public async Task<Result<IReadOnlyList<RoleDto>>> ExecuteAsync(
        GetUserRolesQuery query,
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

        if (!await _userReader.UserExistsAsync(query.TargetUserId, cancellationToken))
            return Result.Failure<IReadOnlyList<RoleDto>>(AuthorizationErrors.UserNotFound);

        if (!await _hierarchy.CanManageUserAsync(query.ActorUserId, query.TargetUserId, cancellationToken))
            return Result.Failure<IReadOnlyList<RoleDto>>(AuthorizationErrors.HierarchyViolation);

        var roles = await _userRoles.GetRolesForUserAsync(query.TargetUserId, cancellationToken);
        var dtos = roles
            .OrderBy(r => r.RoleLevel)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .Select(RoleMapping.ToDto)
            .ToList();

        return Result.Success<IReadOnlyList<RoleDto>>(dtos);
    }
}
