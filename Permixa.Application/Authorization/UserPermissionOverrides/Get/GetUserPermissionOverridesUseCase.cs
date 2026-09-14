using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.UserPermissionOverrides.Get;

public sealed class GetUserPermissionOverridesUseCase
{
    private readonly IEffectivePermissionService _effectivePermissionService;
    private readonly IAuthorizationHierarchyService _hierarchyService;
    private readonly IIdentityUserReader _userReader;
    private readonly IUserPermissionOverrideRepository _overrideRepository;
    private readonly IPermissionRepository _permissionRepository;

    public GetUserPermissionOverridesUseCase(
        IEffectivePermissionService effectivePermissionService,
        IAuthorizationHierarchyService hierarchyService,
        IIdentityUserReader userReader,
        IUserPermissionOverrideRepository overrideRepository,
        IPermissionRepository permissionRepository)
    {
        _effectivePermissionService = effectivePermissionService;
        _hierarchyService = hierarchyService;
        _userReader = userReader;
        _overrideRepository = overrideRepository;
        _permissionRepository = permissionRepository;
    }

    public async Task<Result<IReadOnlyList<UserPermissionOverrideListItemDto>>> ExecuteAsync(
        GetUserPermissionOverridesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.ActorUserId == query.TargetUserId)
        {
            return Result.Failure<IReadOnlyList<UserPermissionOverrideListItemDto>>(
                AuthorizationErrors.CannotManageSelf);
        }

        var permissionCheck = await _effectivePermissionService.HasPermissionAsync(
            query.ActorUserId,
            IamPermissions.UserPermissionOverrides.Manage,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure<IReadOnlyList<UserPermissionOverrideListItemDto>>(permissionCheck.Error!);

        if (!permissionCheck.Value)
        {
            return Result.Failure<IReadOnlyList<UserPermissionOverrideListItemDto>>(
                AuthorizationErrors.MissingManagePermission);
        }

        if (query.TargetUserId == Guid.Empty
            || !await _userReader.UserExistsAsync(query.TargetUserId, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<UserPermissionOverrideListItemDto>>(
                AuthorizationErrors.UserNotFound);
        }

        if (!await _hierarchyService.CanManageUserAsync(
                query.ActorUserId,
                query.TargetUserId,
                cancellationToken))
        {
            return Result.Failure<IReadOnlyList<UserPermissionOverrideListItemDto>>(
                AuthorizationErrors.HierarchyViolation);
        }

        var overrides = await _overrideRepository.GetByUserIdAsync(query.TargetUserId, cancellationToken);
        if (overrides.Count == 0)
            return Result.Success<IReadOnlyList<UserPermissionOverrideListItemDto>>([]);

        var permissionIds = overrides.Select(o => o.PermissionId).Distinct().ToArray();
        var permissions = await _permissionRepository.GetByIdsAsync(permissionIds, cancellationToken);
        var permissionsById = permissions.ToDictionary(p => p.Id);

        var items = overrides
            .Where(o => permissionsById.ContainsKey(o.PermissionId))
            .Select(o =>
            {
                var permission = permissionsById[o.PermissionId];
                return new UserPermissionOverrideListItemDto(
                    o.PermissionId,
                    permission.Name,
                    o.Effect);
            })
            .OrderBy(i => i.PermissionName, StringComparer.Ordinal)
            .ToList();

        return Result.Success<IReadOnlyList<UserPermissionOverrideListItemDto>>(items);
    }
}
