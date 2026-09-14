using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Users;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Sessions.Revoke;

public sealed class RevokeUserSessionUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IIdentityUserReader _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RevokeUserSessionUseCase(
        IEffectivePermissionService effectivePermissions,
        IAuthorizationHierarchyService hierarchy,
        IIdentityUserReader users,
        IRefreshTokenRepository refreshTokens,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchy = hierarchy;
        _users = users;
        _refreshTokens = refreshTokens;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> ExecuteAsync(
        RevokeUserSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var self = UserAdministrationGuard.RejectSelf(command.ActorUserId, command.TargetUserId);
        if (self is not null)
            return self;

        var permission = await UserAdministrationGuard.EnsurePermissionAsync(
            _effectivePermissions, command.ActorUserId, IamPermissions.Sessions.Revoke, cancellationToken);
        if (permission is not null)
            return permission;

        if (!await _users.UserExistsAsync(command.TargetUserId, cancellationToken))
            return Result.Failure(AuthorizationErrors.UserNotFound);

        var owner = await UserAdministrationGuard.RejectOwnerAsync(
            _users, command.TargetUserId, cancellationToken);
        if (owner is not null)
            return owner;

        var hierarchy = await UserAdministrationGuard.EnsureCanManageAsync(
            _hierarchy, command.ActorUserId, command.TargetUserId, cancellationToken);
        if (hierarchy is not null)
            return hierarchy;

        if (!await _refreshTokens.FamilyExistsForUserAsync(
                command.TargetUserId, command.FamilyId, cancellationToken))
        {
            return Result.Failure(AuthorizationErrors.SessionNotFound);
        }

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var now = _clock.UtcNow;
            await _refreshTokens.RevokeFamilyForUserAsync(
                command.TargetUserId, command.FamilyId, now, ct);
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.Sessions.Revoked,
                    now,
                    actorUserId: command.ActorUserId,
                    targetUserId: command.TargetUserId,
                    metadata: new Dictionary<string, string>
                    {
                        ["FamilyId"] = command.FamilyId.ToString()
                    }),
                ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return Result.Success();
    }
}
