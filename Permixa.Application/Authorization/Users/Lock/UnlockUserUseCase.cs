using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Users.Lock;

public sealed class UnlockUserUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IIdentityUserReader _users;
    private readonly IIdentityUserWriter _writer;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UnlockUserUseCase(
        IEffectivePermissionService effectivePermissions,
        IAuthorizationHierarchyService hierarchy,
        IIdentityUserReader users,
        IIdentityUserWriter writer,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchy = hierarchy;
        _users = users;
        _writer = writer;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> ExecuteAsync(
        UnlockUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var self = UserAdministrationGuard.RejectSelf(command.ActorUserId, command.TargetUserId);
        if (self is not null)
            return self;

        var permission = await UserAdministrationGuard.EnsurePermissionAsync(
            _effectivePermissions, command.ActorUserId, IamPermissions.Users.Lock, cancellationToken);
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

        Result? outcome = null;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var mutation = await _writer.UnlockAsync(command.TargetUserId, ct);
            if (mutation == IdentityLockMutation.NotFound)
            {
                outcome = Result.Failure(AuthorizationErrors.UserNotFound);
                return;
            }

            if (mutation == IdentityLockMutation.Unchanged)
            {
                outcome = Result.Success();
                return;
            }

            var now = _clock.UtcNow;
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.Users.Unlocked,
                    now,
                    actorUserId: command.ActorUserId,
                    targetUserId: command.TargetUserId),
                ct);
            await _unitOfWork.SaveChangesAsync(ct);
            outcome = Result.Success();
        }, cancellationToken);

        return outcome ?? Result.Failure(AuthorizationErrors.UserNotFound);
    }
}
