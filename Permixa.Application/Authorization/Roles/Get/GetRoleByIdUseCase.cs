using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Roles.Models;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Roles.Get;

public sealed class GetRoleByIdUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IIdentityRoleReader _roleReader;

    public GetRoleByIdUseCase(
        IEffectivePermissionService effectivePermissions,
        IAuthorizationHierarchyService hierarchy,
        IIdentityRoleReader roleReader)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchy = hierarchy;
        _roleReader = roleReader;
    }

    public async Task<Result<RoleDto>> ExecuteAsync(
        GetRoleByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var permissionCheck = await _effectivePermissions.HasPermissionAsync(
            query.ActorUserId,
            IamPermissions.Roles.Read,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure<RoleDto>(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure<RoleDto>(AuthorizationErrors.MissingManagePermission);

        var role = await _roleReader.GetByIdAsync(query.RoleId, cancellationToken);
        if (role is null)
            return Result.Failure<RoleDto>(AuthorizationErrors.RoleNotFound);

        if (!await _hierarchy.CanManageRoleAsync(query.ActorUserId, role.Id, cancellationToken))
            return Result.Failure<RoleDto>(AuthorizationErrors.HierarchyViolation);

        return Result.Success(RoleMapping.ToDto(role));
    }
}
