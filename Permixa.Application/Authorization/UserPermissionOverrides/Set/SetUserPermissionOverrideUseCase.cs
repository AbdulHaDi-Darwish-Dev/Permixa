using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.UserPermissionOverrides.Models;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authorization;

namespace Permixa.Application.Authorization.UserPermissionOverrides.Set;

public sealed class SetUserPermissionOverrideUseCase
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

    public SetUserPermissionOverrideUseCase(
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

    public async Task<Result<UserPermissionOverrideDto>> ExecuteAsync(
        SetUserPermissionOverrideCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Effect is not (PermissionEffect.Allow or PermissionEffect.Deny))
            return Result.Failure<UserPermissionOverrideDto>(AuthorizationErrors.InvalidPermission);

        if (command.ActorUserId == command.TargetUserId)
            return Result.Failure<UserPermissionOverrideDto>(AuthorizationErrors.CannotManageSelf);

        var permissionCheck = await _effectivePermissionService.HasPermissionAsync(
            command.ActorUserId,
            IamPermissions.UserPermissionOverrides.Manage,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure<UserPermissionOverrideDto>(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure<UserPermissionOverrideDto>(AuthorizationErrors.MissingManagePermission);

        if (!await _userReader.UserExistsAsync(command.TargetUserId, cancellationToken))
            return Result.Failure<UserPermissionOverrideDto>(AuthorizationErrors.UserNotFound);

        var permission = await _permissionRepository.GetByIdAsync(command.PermissionId, cancellationToken);
        if (permission is null)
            return Result.Failure<UserPermissionOverrideDto>(AuthorizationErrors.PermissionNotFound);

        if (!await _hierarchyService.CanManageUserAsync(command.ActorUserId, command.TargetUserId, cancellationToken))
            return Result.Failure<UserPermissionOverrideDto>(AuthorizationErrors.HierarchyViolation);

        var existing = await _overrideRepository.GetAsync(
            command.TargetUserId,
            command.PermissionId,
            cancellationToken);

        if (existing is null)
        {
            var created = UserPermissionOverride.Create(
                command.TargetUserId,
                command.PermissionId,
                command.Effect,
                createdAtUtc: _clock.UtcNow);

            await _overrideRepository.AddAsync(created, cancellationToken);
            await _userAuthorizationVersionStore.IncrementAsync(command.TargetUserId, cancellationToken);
            var now = _clock.UtcNow;
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.UserPermissionOverrides.Set,
                    now,
                    actorUserId: command.ActorUserId,
                    targetUserId: command.TargetUserId,
                    targetPermissionId: command.PermissionId),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(ToDto(created));
        }

        if (existing.Effect == command.Effect)
            return Result.Failure<UserPermissionOverrideDto>(AuthorizationErrors.OverrideUnchanged);

        var changedAt = _clock.UtcNow;
        existing.ChangeEffect(command.Effect, changedAt);
        await _userAuthorizationVersionStore.IncrementAsync(command.TargetUserId, cancellationToken);
        await _audit.WriteAsync(
            IamAuditEvent.Success(
                IamAuditEvents.UserPermissionOverrides.Set,
                changedAt,
                actorUserId: command.ActorUserId,
                targetUserId: command.TargetUserId,
                targetPermissionId: command.PermissionId),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(existing));
    }

    private static UserPermissionOverrideDto ToDto(UserPermissionOverride entity) =>
        new(
            entity.Id,
            entity.UserId,
            entity.PermissionId,
            entity.Effect,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
}
