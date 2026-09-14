using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Permissions.Models;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.RolePermissions.Get;

public sealed class GetRolePermissionsUseCase
{
    private readonly IEffectivePermissionService _effectivePermissionService;
    private readonly IAuthorizationHierarchyService _hierarchyService;
    private readonly IIdentityRoleReader _roleReader;
    private readonly IPermissionRepository _permissionRepository;

    public GetRolePermissionsUseCase(
        IEffectivePermissionService effectivePermissionService,
        IAuthorizationHierarchyService hierarchyService,
        IIdentityRoleReader roleReader,
        IPermissionRepository permissionRepository)
    {
        _effectivePermissionService = effectivePermissionService;
        _hierarchyService = hierarchyService;
        _roleReader = roleReader;
        _permissionRepository = permissionRepository;
    }

    public async Task<Result<IReadOnlyList<PermissionDto>>> ExecuteAsync(
        GetRolePermissionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var permissionCheck = await _effectivePermissionService.HasPermissionAsync(
            query.ActorUserId,
            IamPermissions.Permissions.Read,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure<IReadOnlyList<PermissionDto>>(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure<IReadOnlyList<PermissionDto>>(AuthorizationErrors.MissingManagePermission);

        if (query.RoleId == Guid.Empty
            || !await _roleReader.RoleExistsAsync(query.RoleId, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<PermissionDto>>(AuthorizationErrors.RoleNotFound);
        }

        if (!await _hierarchyService.CanManageRoleAsync(query.ActorUserId, query.RoleId, cancellationToken))
            return Result.Failure<IReadOnlyList<PermissionDto>>(AuthorizationErrors.HierarchyViolation);

        var permissions = await _permissionRepository.GetByRoleIdsAsync([query.RoleId], cancellationToken);
        var dtos = permissions
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(PermissionMapping.ToDto)
            .ToList();

        return Result.Success<IReadOnlyList<PermissionDto>>(dtos);
    }
}
