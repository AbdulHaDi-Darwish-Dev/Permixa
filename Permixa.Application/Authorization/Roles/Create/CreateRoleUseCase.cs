using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Roles.Models;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Roles.Create;

public sealed class CreateRoleUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IAuthorizationHierarchyWriteLock _hierarchyLock;
    private readonly IIdentityRoleReader _roleReader;
    private readonly IIdentityRoleWriter _roleWriter;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateRoleUseCase(
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
        CreateRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = command.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<RoleDto>(AuthorizationErrors.InvalidRoleName);

        if (string.Equals(name, PermixaRoles.Owner, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<RoleDto>(AuthorizationErrors.OwnerProtected);

        if (!Enum.IsDefined(command.Placement))
            return Result.Failure<RoleDto>(AuthorizationErrors.InvalidRolePlacement);

        var permissionCheck = await _effectivePermissions.HasPermissionAsync(
            command.ActorUserId,
            IamPermissions.Roles.Create,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure<RoleDto>(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure<RoleDto>(AuthorizationErrors.MissingManagePermission);

        try
        {
            RoleDto? created = null;

            await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var state = await _hierarchyLock.AcquireAsync(ct)
                    ?? throw new RoleAdministrationFault(AuthorizationErrors.AuthorizationStateUnavailable);

                var roles = await _roleReader.GetAllAsync(ct);
                var reference = roles.FirstOrDefault(r => r.Id == command.ReferenceRoleId)
                    ?? throw new RoleAdministrationFault(AuthorizationErrors.RoleNotFound);

                if (IsOwner(reference)
                    && command.Placement is RolePlacement.Above or RolePlacement.SameLevel)
                {
                    throw new RoleAdministrationFault(AuthorizationErrors.OwnerProtected);
                }

                // Owner cannot CanManageRole(Owner). Below Owner is still allowed when the
                // resulting level is strictly weaker than the actor (typically Owner → 2).
                if (!IsOwner(reference)
                    && !await _hierarchy.CanManageRoleAsync(command.ActorUserId, reference.Id, ct))
                {
                    throw new RoleAdministrationFault(AuthorizationErrors.HierarchyViolation);
                }

                var occupied = RolePlacementCalculator.OccupiedLevels(roles.Select(r => r.RoleLevel));
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

                if (plan.Value.RequiresShift)
                {
                    await _roleWriter.ShiftTiersAsync(plan.Value.ShiftedFromLevels, excludeRoleId: null, ct);
                    state.IncrementRbacVersion();
                }

                var write = await _roleWriter.CreateAsync(name, plan.Value.AssignedLevel, ct);
                if (!write.Succeeded)
                {
                    throw new RoleAdministrationFault(write.Failure switch
                    {
                        IdentityRoleMutationFailure.DuplicateName => AuthorizationErrors.RoleAlreadyExists,
                        IdentityRoleMutationFailure.InvalidName => AuthorizationErrors.InvalidRoleName,
                        _ => AuthorizationErrors.RoleAlreadyExists
                    });
                }

                created = new RoleDto(write.RoleId!.Value, name, plan.Value.AssignedLevel);
                var now = _clock.UtcNow;
                await _audit.WriteAsync(
                    IamAuditEvent.Success(
                        IamAuditEvents.Roles.Created,
                        now,
                        actorUserId: command.ActorUserId,
                        targetRoleId: write.RoleId,
                        metadata: new Dictionary<string, string>
                        {
                            ["Placement"] = command.Placement.ToString(),
                            ["ReferenceRoleId"] = command.ReferenceRoleId.ToString(),
                            ["NewRoleLevel"] = plan.Value.AssignedLevel.ToString(),
                            ["ShiftedTierCount"] = plan.Value.ShiftedFromLevels.Count.ToString()
                        }),
                    ct);
                await _unitOfWork.SaveChangesAsync(ct);
            }, cancellationToken);

            return Result.Success(created!);
        }
        catch (RoleAdministrationFault fault)
        {
            return Result.Failure<RoleDto>(fault.Error);
        }
    }

    private static bool IsOwner(IdentityRoleRecord role) =>
        string.Equals(role.Name, PermixaRoles.Owner, StringComparison.OrdinalIgnoreCase);
}
