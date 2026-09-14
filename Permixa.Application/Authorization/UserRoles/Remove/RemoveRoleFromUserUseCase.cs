using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.UserRoles.Remove;

public sealed class RemoveRoleFromUserUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IIdentityUserReader _userReader;
    private readonly IIdentityRoleReader _roleReader;
    private readonly IIdentityUserRoleReader _userRoles;
    private readonly IIdentityUserRoleWriter _userRoleWriter;
    private readonly IUserAuthorizationVersionStore _userVersions;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RemoveRoleFromUserUseCase(
        IEffectivePermissionService effectivePermissions,
        IAuthorizationHierarchyService hierarchy,
        IIdentityUserReader userReader,
        IIdentityRoleReader roleReader,
        IIdentityUserRoleReader userRoles,
        IIdentityUserRoleWriter userRoleWriter,
        IUserAuthorizationVersionStore userVersions,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchy = hierarchy;
        _userReader = userReader;
        _roleReader = roleReader;
        _userRoles = userRoles;
        _userRoleWriter = userRoleWriter;
        _userVersions = userVersions;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> ExecuteAsync(
        RemoveRoleFromUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ActorUserId == command.TargetUserId)
            return Result.Failure(AuthorizationErrors.CannotManageSelf);

        var permissionCheck = await _effectivePermissions.HasPermissionAsync(
            command.ActorUserId,
            IamPermissions.UserRoles.Manage,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure(AuthorizationErrors.MissingManagePermission);

        if (!await _userReader.UserExistsAsync(command.TargetUserId, cancellationToken))
            return Result.Failure(AuthorizationErrors.UserNotFound);

        var role = await _roleReader.GetByIdAsync(command.RoleId, cancellationToken);
        if (role is null)
            return Result.Failure(AuthorizationErrors.RoleNotFound);

        if (string.Equals(role.Name, PermixaRoles.Owner, StringComparison.OrdinalIgnoreCase))
            return Result.Failure(AuthorizationErrors.OwnerProtected);

        if (!await _hierarchy.CanManageUserAsync(command.ActorUserId, command.TargetUserId, cancellationToken)
            || !await _hierarchy.CanAssignRoleAsync(command.ActorUserId, command.RoleId, cancellationToken))
        {
            return Result.Failure(AuthorizationErrors.HierarchyViolation);
        }

        if (!await _userRoles.IsInRoleAsync(command.TargetUserId, command.RoleId, cancellationToken))
            return Result.Success();

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var removed = await _userRoleWriter.RemoveFromRoleAsync(command.TargetUserId, command.RoleId, ct);
            if (removed)
            {
                await _userVersions.IncrementAsync(command.TargetUserId, ct);
                var now = _clock.UtcNow;
                await _audit.WriteAsync(
                    IamAuditEvent.Success(
                        IamAuditEvents.UserRoles.Removed,
                        now,
                        actorUserId: command.ActorUserId,
                        targetUserId: command.TargetUserId,
                        targetRoleId: command.RoleId),
                    ct);
                await _unitOfWork.SaveChangesAsync(ct);
            }
        }, cancellationToken);

        return Result.Success();
    }
}
