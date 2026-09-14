using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.UserPermissionOverrides.Remove;

public sealed class RemoveUserPermissionOverrideUseCase
{
    private readonly IIdentityUserReader _userReader;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserPermissionOverrideRepository _overrideRepository;
    private readonly IAuthorizationHierarchyService _hierarchyService;
    private readonly IEffectivePermissionService _effectivePermissionService;
    private readonly IUserAuthorizationVersionStore _userAuthorizationVersionStore;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RemoveUserPermissionOverrideUseCase(
        IIdentityUserReader userReader,
        IPermissionRepository permissionRepository,
        IUserPermissionOverrideRepository overrideRepository,
        IAuthorizationHierarchyService hierarchyService,
        IEffectivePermissionService effectivePermissionService,
        IUserAuthorizationVersionStore userAuthorizationVersionStore,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _userReader = userReader;
        _permissionRepository = permissionRepository;
        _overrideRepository = overrideRepository;
        _hierarchyService = hierarchyService;
        _effectivePermissionService = effectivePermissionService;
        _userAuthorizationVersionStore = userAuthorizationVersionStore;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> ExecuteAsync(
        RemoveUserPermissionOverrideCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ActorUserId == command.TargetUserId)
            return Result.Failure(AuthorizationErrors.CannotManageSelf);

        var permissionCheck = await _effectivePermissionService.HasPermissionAsync(
            command.ActorUserId,
            IamPermissions.UserPermissionOverrides.Manage,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure(AuthorizationErrors.MissingManagePermission);

        if (!await _userReader.UserExistsAsync(command.TargetUserId, cancellationToken))
            return Result.Failure(AuthorizationErrors.UserNotFound);

        var permission = await _permissionRepository.GetByIdAsync(command.PermissionId, cancellationToken);
        if (permission is null)
            return Result.Failure(AuthorizationErrors.PermissionNotFound);

        if (!await _hierarchyService.CanManageUserAsync(command.ActorUserId, command.TargetUserId, cancellationToken))
            return Result.Failure(AuthorizationErrors.HierarchyViolation);

        var existing = await _overrideRepository.GetAsync(
            command.TargetUserId,
            command.PermissionId,
            cancellationToken);

        if (existing is null)
            return Result.Failure(AuthorizationErrors.OverrideNotFound);

        await _overrideRepository.RemoveAsync(command.TargetUserId, command.PermissionId, cancellationToken);
        await _userAuthorizationVersionStore.IncrementAsync(command.TargetUserId, cancellationToken);
        var now = _clock.UtcNow;
        await _audit.WriteAsync(
            IamAuditEvent.Success(
                IamAuditEvents.UserPermissionOverrides.Removed,
                now,
                actorUserId: command.ActorUserId,
                targetUserId: command.TargetUserId,
                targetPermissionId: command.PermissionId),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
