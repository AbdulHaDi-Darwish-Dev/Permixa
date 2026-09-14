using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.RolePermissions.Remove;

public sealed class RemovePermissionFromRoleUseCase
{
    private readonly IIdentityRoleReader _roleReader;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IRolePermissionRepository _rolePermissionRepository;
    private readonly IAuthorizationStateRepository _authorizationStateRepository;
    private readonly IAuthorizationHierarchyService _hierarchyService;
    private readonly IEffectivePermissionService _effectivePermissionService;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RemovePermissionFromRoleUseCase(
        IIdentityRoleReader roleReader,
        IPermissionRepository permissionRepository,
        IRolePermissionRepository rolePermissionRepository,
        IAuthorizationStateRepository authorizationStateRepository,
        IAuthorizationHierarchyService hierarchyService,
        IEffectivePermissionService effectivePermissionService,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _roleReader = roleReader;
        _permissionRepository = permissionRepository;
        _rolePermissionRepository = rolePermissionRepository;
        _authorizationStateRepository = authorizationStateRepository;
        _hierarchyService = hierarchyService;
        _effectivePermissionService = effectivePermissionService;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> ExecuteAsync(
        RemovePermissionFromRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var permissionCheck = await _effectivePermissionService.HasPermissionAsync(
            command.ActorUserId,
            IamPermissions.RolePermissions.Manage,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure(AuthorizationErrors.MissingManagePermission);

        if (!await _roleReader.RoleExistsAsync(command.RoleId, cancellationToken))
            return Result.Failure(AuthorizationErrors.RoleNotFound);

        var permission = await _permissionRepository.GetByIdAsync(command.PermissionId, cancellationToken);
        if (permission is null)
            return Result.Failure(AuthorizationErrors.PermissionNotFound);

        if (!await _hierarchyService.CanManageRoleAsync(command.ActorUserId, command.RoleId, cancellationToken))
            return Result.Failure(AuthorizationErrors.HierarchyViolation);

        if (!await _rolePermissionRepository.ExistsAsync(command.RoleId, command.PermissionId, cancellationToken))
            return Result.Failure(AuthorizationErrors.RolePermissionNotFound);

        var state = await _authorizationStateRepository.GetAsync(cancellationToken);
        if (state is null)
            return Result.Failure(AuthorizationErrors.AuthorizationStateUnavailable);

        await _rolePermissionRepository.RemoveAsync(command.RoleId, command.PermissionId, cancellationToken);
        state.IncrementRbacVersion();
        var now = _clock.UtcNow;
        await _audit.WriteAsync(
            IamAuditEvent.Success(
                IamAuditEvents.RolePermissions.Removed,
                now,
                actorUserId: command.ActorUserId,
                targetRoleId: command.RoleId,
                targetPermissionId: command.PermissionId),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
