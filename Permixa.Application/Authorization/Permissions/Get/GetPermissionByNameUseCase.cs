using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Permissions.Models;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Authorization.Permissions.Get;

public sealed class GetPermissionByNameUseCase
{
    private readonly IEffectivePermissionService _effectivePermissionService;
    private readonly IPermissionRepository _permissionRepository;

    public GetPermissionByNameUseCase(
        IEffectivePermissionService effectivePermissionService,
        IPermissionRepository permissionRepository)
    {
        _effectivePermissionService = effectivePermissionService;
        _permissionRepository = permissionRepository;
    }

    public async Task<Result<PermissionDto>> ExecuteAsync(
        GetPermissionByNameQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var permissionCheck = await _effectivePermissionService.HasPermissionAsync(
            query.ActorUserId,
            IamPermissions.Permissions.Read,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure<PermissionDto>(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure<PermissionDto>(AuthorizationErrors.MissingManagePermission);

        var validation = PermissionName.Validate(query.PermissionName);
        if (validation.IsFailure)
            return Result.Failure<PermissionDto>(validation.Error!);

        var name = PermissionName.Normalize(query.PermissionName);
        var permission = await _permissionRepository.GetByNameAsync(name, cancellationToken);
        if (permission is null)
            return Result.Failure<PermissionDto>(AuthorizationErrors.PermissionNotFound);

        return Result.Success(PermissionMapping.ToDto(permission));
    }
}
