using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.EffectivePermissions;

public sealed class GetEffectivePermissionsUseCase
{
    private readonly IEffectivePermissionService _effectivePermissionService;
    private readonly IAuthorizationHierarchyService _hierarchyService;
    private readonly IIdentityUserReader _userReader;

    public GetEffectivePermissionsUseCase(
        IEffectivePermissionService effectivePermissionService,
        IAuthorizationHierarchyService hierarchyService,
        IIdentityUserReader userReader)
    {
        _effectivePermissionService = effectivePermissionService;
        _hierarchyService = hierarchyService;
        _userReader = userReader;
    }

    public async Task<Result<EffectivePermissionNamesDto>> ExecuteAsync(
        GetEffectivePermissionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var permissionCheck = await _effectivePermissionService.HasPermissionAsync(
            query.ActorUserId,
            IamPermissions.Permissions.Read,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure<EffectivePermissionNamesDto>(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure<EffectivePermissionNamesDto>(AuthorizationErrors.MissingManagePermission);

        if (query.TargetUserId == Guid.Empty
            || !await _userReader.UserExistsAsync(query.TargetUserId, cancellationToken))
        {
            return Result.Failure<EffectivePermissionNamesDto>(AuthorizationErrors.UserNotFound);
        }

        if (!await _hierarchyService.CanManageUserAsync(
                query.ActorUserId,
                query.TargetUserId,
                cancellationToken))
        {
            return Result.Failure<EffectivePermissionNamesDto>(AuthorizationErrors.HierarchyViolation);
        }

        var snapshot = await _effectivePermissionService.GetAuthorizationSnapshotAsync(
            query.TargetUserId,
            cancellationToken);

        if (snapshot.IsFailure)
            return Result.Failure<EffectivePermissionNamesDto>(snapshot.Error!);

        return Result.Success(EffectivePermissionNamesDto.FromNames(snapshot.Value.Permissions));
    }
}
