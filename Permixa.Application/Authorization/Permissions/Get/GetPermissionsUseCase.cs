using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Permissions.Models;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Authorization.Permissions.Get;

public sealed class GetPermissionsUseCase
{
    private readonly IEffectivePermissionService _effectivePermissionService;
    private readonly IPermissionRepository _permissionRepository;

    public GetPermissionsUseCase(
        IEffectivePermissionService effectivePermissionService,
        IPermissionRepository permissionRepository)
    {
        _effectivePermissionService = effectivePermissionService;
        _permissionRepository = permissionRepository;
    }

    public async Task<Result<IReadOnlyList<PermissionDto>>> ExecuteAsync(
        GetPermissionsQuery query,
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

        var permissions = await _permissionRepository.GetAllAsync(cancellationToken);
        var dtos = permissions
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(PermissionMapping.ToDto)
            .ToList();

        return Result.Success<IReadOnlyList<PermissionDto>>(dtos);
    }
}
