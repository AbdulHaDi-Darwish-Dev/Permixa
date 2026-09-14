using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Roles.Models;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Roles.Rename;

public sealed class RenameRoleUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IIdentityRoleReader _roleReader;
    private readonly IIdentityRoleWriter _roleWriter;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RenameRoleUseCase(
        IEffectivePermissionService effectivePermissions,
        IAuthorizationHierarchyService hierarchy,
        IIdentityRoleReader roleReader,
        IIdentityRoleWriter roleWriter,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchy = hierarchy;
        _roleReader = roleReader;
        _roleWriter = roleWriter;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<RoleDto>> ExecuteAsync(
        RenameRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = command.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<RoleDto>(AuthorizationErrors.InvalidRoleName);

        var permissionCheck = await _effectivePermissions.HasPermissionAsync(
            command.ActorUserId,
            IamPermissions.Roles.Update,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure<RoleDto>(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure<RoleDto>(AuthorizationErrors.MissingManagePermission);

        var role = await _roleReader.GetByIdAsync(command.RoleId, cancellationToken);
        if (role is null)
            return Result.Failure<RoleDto>(AuthorizationErrors.RoleNotFound);

        if (string.Equals(role.Name, PermixaRoles.Owner, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, PermixaRoles.Owner, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<RoleDto>(AuthorizationErrors.OwnerProtected);
        }

        if (!await _hierarchy.CanManageRoleAsync(command.ActorUserId, role.Id, cancellationToken))
            return Result.Failure<RoleDto>(AuthorizationErrors.HierarchyViolation);

        if (string.Equals(role.Name, name, StringComparison.Ordinal))
            return Result.Success(RoleMapping.ToDto(role));

        var write = await _roleWriter.RenameAsync(role.Id, name, cancellationToken);
        if (!write.Succeeded)
        {
            return Result.Failure<RoleDto>(write.Failure switch
            {
                IdentityRoleMutationFailure.DuplicateName => AuthorizationErrors.RoleAlreadyExists,
                IdentityRoleMutationFailure.InvalidName => AuthorizationErrors.InvalidRoleName,
                IdentityRoleMutationFailure.NotFound => AuthorizationErrors.RoleNotFound,
                _ => AuthorizationErrors.RoleAlreadyExists
            });
        }

        var now = _clock.UtcNow;
        await _audit.WriteAsync(
            IamAuditEvent.Success(
                IamAuditEvents.Roles.Renamed,
                now,
                actorUserId: command.ActorUserId,
                targetRoleId: role.Id),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new RoleDto(role.Id, name, role.RoleLevel));
    }
}
