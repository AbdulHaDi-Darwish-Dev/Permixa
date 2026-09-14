using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.UserRoles.Get;

public sealed class GetUsersInRoleUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IIdentityRoleReader _roleReader;
    private readonly IIdentityUserRoleReader _userRoles;

    public GetUsersInRoleUseCase(
        IEffectivePermissionService effectivePermissions,
        IAuthorizationHierarchyService hierarchy,
        IIdentityRoleReader roleReader,
        IIdentityUserRoleReader userRoles)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchy = hierarchy;
        _roleReader = roleReader;
        _userRoles = userRoles;
    }

    public async Task<Result<IReadOnlyList<IamUserListItemDto>>> ExecuteAsync(
        GetUsersInRoleQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var permissionCheck = await _effectivePermissions.HasPermissionAsync(
            query.ActorUserId,
            IamPermissions.Roles.Read,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure<IReadOnlyList<IamUserListItemDto>>(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure<IReadOnlyList<IamUserListItemDto>>(AuthorizationErrors.MissingManagePermission);

        if (!await _roleReader.RoleExistsAsync(query.RoleId, cancellationToken))
            return Result.Failure<IReadOnlyList<IamUserListItemDto>>(AuthorizationErrors.RoleNotFound);

        if (!await _hierarchy.CanManageRoleAsync(query.ActorUserId, query.RoleId, cancellationToken))
            return Result.Failure<IReadOnlyList<IamUserListItemDto>>(AuthorizationErrors.HierarchyViolation);

        var users = await _userRoles.GetUsersInRoleAsync(query.RoleId, cancellationToken);
        var dtos = users
            .Select(u => new IamUserListItemDto(u.UserId, u.UserName, u.Email))
            .OrderBy(u => u.UserName, StringComparer.Ordinal)
            .ThenBy(u => u.UserId)
            .ToList();

        return Result.Success<IReadOnlyList<IamUserListItemDto>>(dtos);
    }
}
