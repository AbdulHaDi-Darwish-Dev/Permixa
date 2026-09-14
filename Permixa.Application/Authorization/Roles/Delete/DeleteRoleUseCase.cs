using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Roles.Delete;

public sealed class DeleteRoleUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IIdentityRoleReader _roleReader;
    private readonly IIdentityRoleWriter _roleWriter;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public DeleteRoleUseCase(
        IEffectivePermissionService effectivePermissions,
        IAuthorizationHierarchyService hierarchy,
        IIdentityRoleReader roleReader,
        IIdentityRoleWriter roleWriter,
        IRolePermissionRepository rolePermissions,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchy = hierarchy;
        _roleReader = roleReader;
        _roleWriter = roleWriter;
        _rolePermissions = rolePermissions;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> ExecuteAsync(
        DeleteRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var permissionCheck = await _effectivePermissions.HasPermissionAsync(
            command.ActorUserId,
            IamPermissions.Roles.Delete,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure(AuthorizationErrors.MissingManagePermission);

        var role = await _roleReader.GetByIdAsync(command.RoleId, cancellationToken);
        if (role is null)
            return Result.Failure(AuthorizationErrors.RoleNotFound);

        if (string.Equals(role.Name, PermixaRoles.Owner, StringComparison.OrdinalIgnoreCase))
            return Result.Failure(AuthorizationErrors.OwnerProtected);

        if (!await _hierarchy.CanManageRoleAsync(command.ActorUserId, role.Id, cancellationToken))
            return Result.Failure(AuthorizationErrors.HierarchyViolation);

        if (await _roleReader.HasAssignedUsersAsync(role.Id, cancellationToken))
            return Result.Failure(AuthorizationErrors.RoleHasUsers);

        if (await _rolePermissions.ExistsByRoleIdAsync(role.Id, cancellationToken))
            return Result.Failure(AuthorizationErrors.RoleHasPermissions);

        var write = await _roleWriter.DeleteAsync(role.Id, cancellationToken);
        if (!write.Succeeded)
            return Result.Failure(AuthorizationErrors.RoleNotFound);

        var now = _clock.UtcNow;
        await _audit.WriteAsync(
            IamAuditEvent.Success(
                IamAuditEvents.Roles.Deleted,
                now,
                actorUserId: command.ActorUserId,
                targetRoleId: role.Id),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
