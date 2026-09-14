using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Application.Verification.EmailChange;

namespace Permixa.Application.Authorization.Users.ChangeEmail;

public sealed class AdminRequestEmailChangeUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IIdentityUserReader _users;
    private readonly PendingEmailChangeService _pending;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public AdminRequestEmailChangeUseCase(
        IEffectivePermissionService effectivePermissions,
        IAuthorizationHierarchyService hierarchy,
        IIdentityUserReader users,
        PendingEmailChangeService pending,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchy = hierarchy;
        _users = users;
        _pending = pending;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> ExecuteAsync(
        AdminRequestEmailChangeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var self = UserAdministrationGuard.RejectSelf(command.ActorUserId, command.TargetUserId);
        if (self is not null)
            return self;

        var permission = await UserAdministrationGuard.EnsurePermissionAsync(
            _effectivePermissions,
            command.ActorUserId,
            IamPermissions.Users.ChangeEmail,
            cancellationToken);
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

        var result = await _pending.RequestAsync(command.TargetUserId, command.NewEmail, cancellationToken);
        if (result.IsSuccess)
        {
            var now = _clock.UtcNow;
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.Credentials.EmailChangeAdminRequested,
                    now,
                    actorUserId: command.ActorUserId,
                    targetUserId: command.TargetUserId),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
