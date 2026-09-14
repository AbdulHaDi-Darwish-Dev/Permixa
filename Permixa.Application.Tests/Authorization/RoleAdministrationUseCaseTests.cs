using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Roles.ChangePosition;
using Permixa.Application.Authorization.Roles.Create;
using Permixa.Application.Authorization.Roles.Delete;
using Permixa.Application.Authorization.Roles.Get;
using Permixa.Application.Authorization.Roles.Rename;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authorization;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class RoleAdministrationUseCaseTests
{
    private readonly Mock<IEffectivePermissionService> _permissions = new();
    private readonly Mock<IAuthorizationHierarchyService> _hierarchy = new();
    private readonly Mock<IAuthorizationHierarchyWriteLock> _lock = new();
    private readonly Mock<IIdentityRoleReader> _roles = new();
    private readonly Mock<IIdentityRoleWriter> _writer = new();
    private readonly Mock<IRolePermissionRepository> _rolePermissions = new();
    private readonly Mock<IIamAuditSink> _audit = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IRoleHierarchyReader> _hierarchyReader = new();

    private readonly Guid _actor = Guid.NewGuid();
    private readonly IdentityRoleRecord _owner = new(Guid.NewGuid(), PermixaRoles.Owner, 1);
    private readonly IdentityRoleRecord _admin = new(Guid.NewGuid(), "Admin", 10);
    private readonly IdentityRoleRecord _manager = new(Guid.NewGuid(), "Manager", 20);
    private readonly AuthorizationState _state = AuthorizationState.CreateInitial();

    public RoleAdministrationUseCaseTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        _uow.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((action, ct) => action(ct));
        _lock.Setup(l => l.AcquireAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_state);
        _roles.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IdentityRoleRecord> { _owner, _admin, _manager });
        _hierarchy.Setup(h => h.CanManageRoleAsync(_actor, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _hierarchy.Setup(h => h.CanCreateOrChangeRoleToLevelAsync(_actor, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        Grant(IamPermissions.Roles.Create, true);
        Grant(IamPermissions.Roles.Read, true);
        Grant(IamPermissions.Roles.Update, true);
        Grant(IamPermissions.Roles.Delete, true);
    }

    [Fact]
    public async Task Create_SameLevel_DoesNotIncrementRbacVersion()
    {
        SetupCreate("Supervisor");

        var result = await CreateRoleSut().ExecuteAsync(
            new CreateRoleCommand(_actor, "Supervisor", _manager.Id, RolePlacement.SameLevel));

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Value.RoleLevel);
        Assert.Equal(AuthorizationState.InitialRbacVersion, _state.RbacVersion);
        _writer.Verify(
            w => w.ShiftTiersAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_BelowFreeSlot_DoesNotIncrementRbacVersion()
    {
        SetupCreate("Supervisor");

        var result = await CreateRoleSut().ExecuteAsync(
            new CreateRoleCommand(_actor, "Supervisor", _admin.Id, RolePlacement.Below));

        Assert.True(result.IsSuccess);
        Assert.Equal(11, result.Value.RoleLevel);
        Assert.Equal(AuthorizationState.InitialRbacVersion, _state.RbacVersion);
        _writer.Verify(
            w => w.ShiftTiersAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_AboveRequiringShift_IncrementsRbacVersionOnce()
    {
        SetupCreate("TeamLead");

        var result = await CreateRoleSut().ExecuteAsync(
            new CreateRoleCommand(_actor, "TeamLead", _manager.Id, RolePlacement.Above));

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Value.RoleLevel);
        Assert.Equal(AuthorizationState.InitialRbacVersion + 1, _state.RbacVersion);
        _writer.Verify(
            w => w.ShiftTiersAsync(It.Is<IReadOnlyCollection<int>>(levels => levels.Single() == 20), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_BelowRequiringShift_IncrementsRbacVersionOnce()
    {
        var supervisor = new IdentityRoleRecord(Guid.NewGuid(), "Supervisor", 11);
        _roles.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IdentityRoleRecord> { _owner, _admin, supervisor, _manager });
        SetupCreate("Lead");

        var result = await CreateRoleSut().ExecuteAsync(
            new CreateRoleCommand(_actor, "Lead", _admin.Id, RolePlacement.Below));

        Assert.True(result.IsSuccess);
        Assert.Equal(11, result.Value.RoleLevel);
        Assert.Equal(AuthorizationState.InitialRbacVersion + 1, _state.RbacVersion);
        _writer.Verify(
            w => w.ShiftTiersAsync(It.Is<IReadOnlyCollection<int>>(levels => levels.SequenceEqual(new[] { 11 })), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_SameLevelOwner_IsProtected()
    {
        var result = await CreateRoleSut().ExecuteAsync(
            new CreateRoleCommand(_actor, "Peer", _owner.Id, RolePlacement.SameLevel));

        Assert.Equal(AuthorizationErrors.OwnerProtected, result.Error);
        _writer.Verify(
            w => w.CreateAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_AboveOwner_IsProtected()
    {
        var result = await CreateRoleSut().ExecuteAsync(
            new CreateRoleCommand(_actor, "Root", _owner.Id, RolePlacement.Above));

        Assert.Equal(AuthorizationErrors.OwnerProtected, result.Error);
    }

    [Fact]
    public async Task Create_ResultAtOrAboveActor_IsHierarchyViolation()
    {
        _hierarchy.Setup(h => h.CanCreateOrChangeRoleToLevelAsync(_actor, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateRoleSut().ExecuteAsync(
            new CreateRoleCommand(_actor, "Peer", _admin.Id, RolePlacement.SameLevel));

        Assert.Equal(AuthorizationErrors.HierarchyViolation, result.Error);
    }

    [Fact]
    public async Task ChangePosition_RealMove_IncrementsOnce()
    {
        var result = await ChangePositionSut().ExecuteAsync(
            new ChangeRolePositionCommand(_actor, _manager.Id, _admin.Id, RolePlacement.Below));

        Assert.True(result.IsSuccess);
        Assert.Equal(11, result.Value.RoleLevel);
        Assert.Equal(AuthorizationState.InitialRbacVersion + 1, _state.RbacVersion);
    }

    [Fact]
    public async Task ChangePosition_WithMultipleShiftedTiers_StillIncrementsOnce()
    {
        var lead = new IdentityRoleRecord(Guid.NewGuid(), "Lead", 11);
        var clerk = new IdentityRoleRecord(Guid.NewGuid(), "Clerk", 12);
        _roles.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IdentityRoleRecord> { _owner, _admin, lead, clerk, _manager });

        var result = await ChangePositionSut().ExecuteAsync(
            new ChangeRolePositionCommand(_actor, _manager.Id, _admin.Id, RolePlacement.Below));

        Assert.True(result.IsSuccess);
        Assert.Equal(11, result.Value.RoleLevel);
        Assert.Equal(AuthorizationState.InitialRbacVersion + 1, _state.RbacVersion);
        _writer.Verify(
            w => w.ShiftTiersAsync(
                It.Is<IReadOnlyCollection<int>>(levels => levels.SequenceEqual(new[] { 11, 12 })),
                _manager.Id,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ChangePosition_NoOp_DoesNotIncrement()
    {
        var result = await ChangePositionSut().ExecuteAsync(
            new ChangeRolePositionCommand(_actor, _manager.Id, _manager.Id, RolePlacement.SameLevel));

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Value.RoleLevel);
        Assert.Equal(AuthorizationState.InitialRbacVersion, _state.RbacVersion);
        _writer.Verify(
            w => w.SetRoleLevelAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ChangePosition_CannotMoveOwner()
    {
        var result = await ChangePositionSut().ExecuteAsync(
            new ChangeRolePositionCommand(_actor, _owner.Id, _admin.Id, RolePlacement.Below));

        Assert.Equal(AuthorizationErrors.OwnerProtected, result.Error);
    }

    [Fact]
    public async Task Rename_Owner_IsProtected()
    {
        _roles.Setup(r => r.GetByIdAsync(_owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_owner);

        var result = await RenameSut().ExecuteAsync(new RenameRoleCommand(_actor, _owner.Id, "Root"));

        Assert.Equal(AuthorizationErrors.OwnerProtected, result.Error);
    }

    [Fact]
    public async Task Delete_Owner_IsProtected()
    {
        _roles.Setup(r => r.GetByIdAsync(_owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_owner);

        var result = await DeleteSut().ExecuteAsync(new DeleteRoleCommand(_actor, _owner.Id));

        Assert.Equal(AuthorizationErrors.OwnerProtected, result.Error);
    }

    [Fact]
    public async Task Delete_WithUsers_IsRejected()
    {
        _roles.Setup(r => r.GetByIdAsync(_manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_manager);
        _roles.Setup(r => r.HasAssignedUsersAsync(_manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await DeleteSut().ExecuteAsync(new DeleteRoleCommand(_actor, _manager.Id));

        Assert.Equal(AuthorizationErrors.RoleHasUsers, result.Error);
    }

    [Fact]
    public async Task Delete_WithPermissions_IsRejected()
    {
        _roles.Setup(r => r.GetByIdAsync(_manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_manager);
        _roles.Setup(r => r.HasAssignedUsersAsync(_manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _rolePermissions.Setup(r => r.ExistsByRoleIdAsync(_manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await DeleteSut().ExecuteAsync(new DeleteRoleCommand(_actor, _manager.Id));

        Assert.Equal(AuthorizationErrors.RoleHasPermissions, result.Error);
    }

    [Fact]
    public async Task GetRoles_ReturnsOnlyHierarchicallyWeakerRoles()
    {
        _hierarchyReader.Setup(r => r.GetEffectiveUserLevelAsync(_actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);

        var result = await new GetRolesUseCase(_permissions.Object, _hierarchyReader.Object, _roles.Object)
            .ExecuteAsync(new GetRolesQuery(_actor));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("Manager", result.Value[0].Name);
    }

    private void Grant(string permission, bool allowed) =>
        _permissions.Setup(p => p.HasPermissionAsync(_actor, permission, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(allowed));

    private void SetupCreate(string name) =>
        _writer.Setup(w => w.CreateAsync(name, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityRoleMutationResult.Success(Guid.NewGuid()));

    private CreateRoleUseCase CreateRoleSut() =>
        new(_permissions.Object, _hierarchy.Object, _lock.Object, _roles.Object, _writer.Object, _audit.Object, _uow.Object, _clock.Object);

    private ChangeRolePositionUseCase ChangePositionSut() =>
        new(_permissions.Object, _hierarchy.Object, _lock.Object, _roles.Object, _writer.Object, _audit.Object, _uow.Object, _clock.Object);

    private RenameRoleUseCase RenameSut() =>
        new(_permissions.Object, _hierarchy.Object, _roles.Object, _writer.Object, _audit.Object, _uow.Object, _clock.Object);

    private DeleteRoleUseCase DeleteSut() =>
        new(_permissions.Object, _hierarchy.Object, _roles.Object, _writer.Object, _rolePermissions.Object, _audit.Object, _uow.Object, _clock.Object);
}
