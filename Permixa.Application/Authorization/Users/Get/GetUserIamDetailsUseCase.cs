using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Roles.Models;
using Permixa.Application.Authorization.UserPermissionOverrides.Get;
using Permixa.Application.Authorization.Users.Models;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Users.Get;

public sealed class GetUserIamDetailsUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IIdentityUserReader _users;
    private readonly IIdentityUserRoleReader _userRoles;
    private readonly IUserPermissionOverrideRepository _overrides;
    private readonly IPermissionRepository _permissions;
    private readonly IClock _clock;

    public GetUserIamDetailsUseCase(
        IEffectivePermissionService effectivePermissions,
        IAuthorizationHierarchyService hierarchy,
        IIdentityUserReader users,
        IIdentityUserRoleReader userRoles,
        IUserPermissionOverrideRepository overrides,
        IPermissionRepository permissions,
        IClock clock)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchy = hierarchy;
        _users = users;
        _userRoles = userRoles;
        _overrides = overrides;
        _permissions = permissions;
        _clock = clock;
    }

    public async Task<Result<UserIamDetailsDto>> ExecuteAsync(
        GetUserIamDetailsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var self = UserAdministrationGuard.RejectSelf(query.ActorUserId, query.TargetUserId);
        if (self is not null)
            return Result.Failure<UserIamDetailsDto>(self.Error!);

        var permission = await UserAdministrationGuard.EnsurePermissionAsync(
            _effectivePermissions, query.ActorUserId, IamPermissions.Users.Read, cancellationToken);
        if (permission is not null)
            return Result.Failure<UserIamDetailsDto>(permission.Error!);

        var user = await _users.GetIamUserByIdAsync(query.TargetUserId, _clock.UtcNow, cancellationToken);
        if (user is null)
            return Result.Failure<UserIamDetailsDto>(AuthorizationErrors.UserNotFound);

        var hierarchy = await UserAdministrationGuard.EnsureCanManageAsync(
            _hierarchy, query.ActorUserId, query.TargetUserId, cancellationToken);
        if (hierarchy is not null)
            return Result.Failure<UserIamDetailsDto>(hierarchy.Error!);

        var roles = await _userRoles.GetRolesForUserAsync(query.TargetUserId, cancellationToken);
        var roleDtos = roles
            .OrderBy(r => r.RoleLevel)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .Select(RoleMapping.ToDto)
            .ToList();

        var overrideRows = await _overrides.GetByUserIdAsync(query.TargetUserId, cancellationToken);
        var permissionIds = overrideRows.Select(o => o.PermissionId).Distinct().ToArray();
        var permissionById = (await _permissions.GetByIdsAsync(permissionIds, cancellationToken))
            .ToDictionary(p => p.Id);

        var overrideDtos = overrideRows
            .Select(o =>
            {
                permissionById.TryGetValue(o.PermissionId, out var permissionEntity);
                return new UserPermissionOverrideListItemDto(
                    o.PermissionId,
                    permissionEntity?.Name ?? o.PermissionId.ToString("D"),
                    o.Effect);
            })
            .OrderBy(o => o.PermissionName, StringComparer.Ordinal)
            .ToList();

        var snapshot = await _effectivePermissions.GetAuthorizationSnapshotAsync(
            query.TargetUserId,
            cancellationToken);
        if (snapshot.IsFailure)
            return Result.Failure<UserIamDetailsDto>(snapshot.Error!);

        return Result.Success(new UserIamDetailsDto(
            UserMapping.ToDto(user),
            roleDtos,
            overrideDtos,
            snapshot.Value.Permissions.OrderBy(name => name, StringComparer.Ordinal).ToList()));
    }
}
