using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Authorization.EffectivePermissions;

public sealed class GetMyEffectivePermissionsUseCase
{
    private readonly IEffectivePermissionService _effectivePermissionService;

    public GetMyEffectivePermissionsUseCase(IEffectivePermissionService effectivePermissionService)
    {
        _effectivePermissionService = effectivePermissionService;
    }

    public async Task<Result<EffectivePermissionNamesDto>> ExecuteAsync(
        GetMyEffectivePermissionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var snapshot = await _effectivePermissionService.GetAuthorizationSnapshotAsync(
            query.UserId,
            cancellationToken);

        if (snapshot.IsFailure)
            return Result.Failure<EffectivePermissionNamesDto>(snapshot.Error!);

        return Result.Success(EffectivePermissionNamesDto.FromNames(snapshot.Value.Permissions));
    }
}
