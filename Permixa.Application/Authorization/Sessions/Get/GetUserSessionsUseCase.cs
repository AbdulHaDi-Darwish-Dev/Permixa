using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Sessions.Models;
using Permixa.Application.Authorization.Users;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Sessions.Get;

public sealed class GetUserSessionsUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IIdentityUserReader _users;
    private readonly ISessionReader _sessions;
    private readonly IClock _clock;

    public GetUserSessionsUseCase(
        IEffectivePermissionService effectivePermissions,
        IAuthorizationHierarchyService hierarchy,
        IIdentityUserReader users,
        ISessionReader sessions,
        IClock clock)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchy = hierarchy;
        _users = users;
        _sessions = sessions;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<SessionDto>>> ExecuteAsync(
        GetUserSessionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var self = UserAdministrationGuard.RejectSelf(query.ActorUserId, query.TargetUserId);
        if (self is not null)
            return Result.Failure<IReadOnlyList<SessionDto>>(self.Error!);

        var permission = await UserAdministrationGuard.EnsurePermissionAsync(
            _effectivePermissions, query.ActorUserId, IamPermissions.Sessions.Read, cancellationToken);
        if (permission is not null)
            return Result.Failure<IReadOnlyList<SessionDto>>(permission.Error!);

        if (!await _users.UserExistsAsync(query.TargetUserId, cancellationToken))
            return Result.Failure<IReadOnlyList<SessionDto>>(AuthorizationErrors.UserNotFound);

        var owner = await UserAdministrationGuard.RejectOwnerAsync(
            _users, query.TargetUserId, cancellationToken);
        if (owner is not null)
            return Result.Failure<IReadOnlyList<SessionDto>>(owner.Error!);

        var hierarchy = await UserAdministrationGuard.EnsureCanManageAsync(
            _hierarchy, query.ActorUserId, query.TargetUserId, cancellationToken);
        if (hierarchy is not null)
            return Result.Failure<IReadOnlyList<SessionDto>>(hierarchy.Error!);

        var families = await _sessions.GetActiveFamiliesForUserAsync(
            query.TargetUserId, _clock.UtcNow, cancellationToken);

        return Result.Success<IReadOnlyList<SessionDto>>(
            families.Select(f => SessionMapping.ToDto(f, currentFamilyId: null)).ToList());
    }
}
