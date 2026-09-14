using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Roles.Models;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Roles.ChangePosition;

public sealed class ChangeRolePositionUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IAuthorizationHierarchyWriteLock _hierarchyLock;
    private readonly IIdentityRoleReader _roleReader;
    private readonly IIdentityRoleWriter _roleWriter;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ChangeRolePositionUseCase(
        IEffectivePermissionService effectivePermissions,
        IAuthorizationHierarchyService hierarchy,
        IAuthorizationHierarchyWriteLock hierarchyLock,
        IIdentityRoleReader roleReader,
        IIdentityRoleWriter roleWriter,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchy = hierarchy;
        _hierarchyLock = hierarchyLock;
        _roleReader = roleReader;
        _roleWriter = roleWriter;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<RoleDto>> ExecuteAsync(
        ChangeRolePositionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!Enum.IsDefined(command.Placement))
            return Result.Failure<RoleDto>(AuthorizationErrors.InvalidRolePlacement);

        var permissionCheck = await _effectivePermissions.HasPermissionAsync(
            command.ActorUserId,
            IamPermissions.Roles.Update,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure<RoleDto>(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure<RoleDto>(AuthorizationErrors.MissingManagePermission);

        try
        {
            RoleDto? updated = null;

            await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var state = await _hierarchyLock.AcquireAsync(ct)
                    ?? throw new RoleAdministrationFault(AuthorizationErrors.AuthorizationStateUnavailable);

                var roles = await _roleReader.GetAllAsync(ct);
                var target = roles.FirstOrDefault(r => r.Id == command.RoleId)
                    ?? throw new RoleAdministrationFault(AuthorizationErrors.RoleNotFound);
                var reference = roles.FirstOrDefault(r => r.Id == command.ReferenceRoleId)
                    ?? throw new RoleAdministrationFault(AuthorizationErrors.RoleNotFound);

                if (IsOwner(target))
                    throw new RoleAdministrationFault(AuthorizationErrors.OwnerProtected);

                if (IsOwner(reference)
                    && command.Placement is RolePlacement.Above or RolePlacement.SameLevel)
                {
                    throw new RoleAdministrationFault(AuthorizationErrors.OwnerProtected);
                }

                if (command.RoleId == command.ReferenceRoleId
                    && command.Placement is not RolePlacement.SameLevel)
                {
                    throw new RoleAdministrationFault(AuthorizationErrors.InvalidRolePlacement);
                }

                if (!await _hierarchy.CanManageRoleAsync(command.ActorUserId, target.Id, ct))
                    throw new RoleAdministrationFault(AuthorizationErrors.HierarchyViolation);

                // Owner cannot CanManageRole(Owner). Below Owner is allowed when the
                // resulting level is strictly weaker than the actor.
                if (!IsOwner(reference)
                    && !await _hierarchy.CanManageRoleAsync(command.ActorUserId, reference.Id, ct))
                {
                    throw new RoleAdministrationFault(AuthorizationErrors.HierarchyViolation);
                }

                var occupied = RolePlacementCalculator.OccupiedLevels(
                    roles.Select(r => r.RoleLevel),
                    excludeOneOccurrenceOfLevel: target.RoleLevel);

                var plan = RolePlacementCalculator.Plan(command.Placement, reference.RoleLevel, occupied);
                if (plan.IsFailure)
                    throw new RoleAdministrationFault(plan.Error!);

                if (!await _hierarchy.CanCreateOrChangeRoleToLevelAsync(
                        command.ActorUserId,
                        plan.Value.AssignedLevel,
                        ct))
                {
                    throw new RoleAdministrationFault(AuthorizationErrors.HierarchyViolation);
                }

                var noOp = plan.Value.AssignedLevel == target.RoleLevel && !plan.Value.RequiresShift;
                if (noOp)
                {
                    updated = RoleMapping.ToDto(target);
                    return;
                }

                if (plan.Value.RequiresShift)
                    await _roleWriter.ShiftTiersAsync(plan.Value.ShiftedFromLevels, target.Id, ct);

                var oldLevel = target.RoleLevel;
                await _roleWriter.SetRoleLevelAsync(target.Id, plan.Value.AssignedLevel, ct);
                state.IncrementRbacVersion();
                var now = _clock.UtcNow;
                await _audit.WriteAsync(
                    IamAuditEvent.Success(
                        IamAuditEvents.Roles.Repositioned,
                        now,
                        actorUserId: command.ActorUserId,
                        targetRoleId: target.Id,
                        metadata: new Dictionary<string, string>
                        {
                            ["OldRoleLevel"] = oldLevel.ToString(),
                            ["NewRoleLevel"] = plan.Value.AssignedLevel.ToString(),
                            ["Placement"] = command.Placement.ToString(),
                            ["ReferenceRoleId"] = command.ReferenceRoleId.ToString(),
                            ["ShiftedTierCount"] = plan.Value.ShiftedFromLevels.Count.ToString()
                        }),
                    ct);
                await _unitOfWork.SaveChangesAsync(ct);
                updated = new RoleDto(target.Id, target.Name, plan.Value.AssignedLevel);
            }, cancellationToken);

            return Result.Success(updated!);
        }
        catch (RoleAdministrationFault fault)
        {
            return Result.Failure<RoleDto>(fault.Error);
        }
    }

    private static bool IsOwner(IdentityRoleRecord role) =>
        string.Equals(role.Name, PermixaRoles.Owner, StringComparison.OrdinalIgnoreCase);
}
