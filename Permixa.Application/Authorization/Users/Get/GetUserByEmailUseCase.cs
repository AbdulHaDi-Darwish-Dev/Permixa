using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Users.Models;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Users.Get;

public sealed class GetUserByEmailUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IIdentityUserReader _users;
    private readonly IClock _clock;

    public GetUserByEmailUseCase(
        IEffectivePermissionService effectivePermissions,
        IAuthorizationHierarchyService hierarchy,
        IIdentityUserReader users,
        IClock clock)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchy = hierarchy;
        _users = users;
        _clock = clock;
    }

    public async Task<Result<IamUserDto>> ExecuteAsync(
        GetUserByEmailQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var permission = await UserAdministrationGuard.EnsurePermissionAsync(
            _effectivePermissions, query.ActorUserId, IamPermissions.Users.Read, cancellationToken);
        if (permission is not null)
            return Result.Failure<IamUserDto>(permission.Error!);

        var email = query.Email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure<IamUserDto>(AuthorizationErrors.UserNotFound);

        var user = await _users.GetIamUserByEmailAsync(email, _clock.UtcNow, cancellationToken);
        if (user is null)
            return Result.Failure<IamUserDto>(AuthorizationErrors.UserNotFound);

        var hierarchy = await UserAdministrationGuard.EnsureCanManageAsync(
            _hierarchy, query.ActorUserId, user.Id, cancellationToken);
        if (hierarchy is not null)
            return Result.Failure<IamUserDto>(hierarchy.Error!);

        return Result.Success(UserMapping.ToDto(user));
    }
}
