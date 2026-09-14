using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.UserRoles.Assign;
using Permixa.Application.Authorization.UserRoles.Get;
using Permixa.Application.Authorization.UserRoles.Remove;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class UserRoleUseCaseTests
{
    private readonly Mock<IEffectivePermissionService> _permissions = new();
    private readonly Mock<IAuthorizationHierarchyService> _hierarchy = new();
    private readonly Mock<IIdentityUserReader> _users = new();
    private readonly Mock<IIdentityRoleReader> _roles = new();
    private readonly Mock<IIdentityUserRoleReader> _memberships = new();
    private readonly Mock<IIdentityUserRoleWriter> _writer = new();
    private readonly Mock<IUserAuthorizationVersionStore> _versions = new();
    private readonly Mock<IIamAuditSink> _audit = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IClock> _clock = new();

    private readonly Guid _actor = Guid.NewGuid();
    private readonly Guid _target = Guid.NewGuid();
    private readonly IdentityRoleRecord _manager = new(Guid.NewGuid(), "Manager", 20);
    private readonly IdentityRoleRecord _owner = new(Guid.NewGuid(), PermixaRoles.Owner, 1);

    public UserRoleUseCaseTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        _uow.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((action, ct) => action(ct));
        _permissions.Setup(p => p.HasPermissionAsync(_actor, IamPermissions.UserRoles.Manage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));
        _permissions.Setup(p => p.HasPermissionAsync(_actor, IamPermissions.Roles.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));
        _users.Setup(u => u.UserExistsAsync(_target, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.UserExistsAsync(_actor, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _roles.Setup(r => r.GetByIdAsync(_manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_manager);
        _roles.Setup(r => r.GetByIdAsync(_owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_owner);
        _roles.Setup(r => r.RoleExistsAsync(_manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _hierarchy.Setup(h => h.CanManageUserAsync(_actor, _target, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _hierarchy.Setup(h => h.CanAssignRoleAsync(_actor, _manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _hierarchy.Setup(h => h.CanManageRoleAsync(_actor, _manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    [Fact]
    public async Task Assign_RealChange_IncrementsUserVersionOnce()
    {
        _memberships.Setup(m => m.IsInRoleAsync(_target, _manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _writer.Setup(w => w.AddToRoleAsync(_target, _manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await AssignSut().ExecuteAsync(new AssignRoleToUserCommand(_actor, _target, _manager.Id));

        Assert.True(result.IsSuccess);
        _versions.Verify(v => v.IncrementAsync(_target, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assign_AlreadyMember_IsIdempotent()
    {
        _memberships.Setup(m => m.IsInRoleAsync(_target, _manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await AssignSut().ExecuteAsync(new AssignRoleToUserCommand(_actor, _target, _manager.Id));

        Assert.True(result.IsSuccess);
        _writer.Verify(w => w.AddToRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _versions.Verify(v => v.IncrementAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_Owner_IsProtected()
    {
        var result = await AssignSut().ExecuteAsync(new AssignRoleToUserCommand(_actor, _target, _owner.Id));

        Assert.Equal(AuthorizationErrors.OwnerProtected, result.Error);
    }

    [Fact]
    public async Task Assign_Self_IsForbidden()
    {
        var result = await AssignSut().ExecuteAsync(new AssignRoleToUserCommand(_actor, _actor, _manager.Id));

        Assert.Equal(AuthorizationErrors.CannotManageSelf, result.Error);
    }

    [Fact]
    public async Task Remove_MissingMembership_IsIdempotent()
    {
        _memberships.Setup(m => m.IsInRoleAsync(_target, _manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await RemoveSut().ExecuteAsync(new RemoveRoleFromUserCommand(_actor, _target, _manager.Id));

        Assert.True(result.IsSuccess);
        _writer.Verify(w => w.RemoveFromRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _versions.Verify(v => v.IncrementAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Remove_RealChange_IncrementsUserVersionOnce()
    {
        _memberships.Setup(m => m.IsInRoleAsync(_target, _manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _writer.Setup(w => w.RemoveFromRoleAsync(_target, _manager.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await RemoveSut().ExecuteAsync(new RemoveRoleFromUserCommand(_actor, _target, _manager.Id));

        Assert.True(result.IsSuccess);
        _versions.Verify(v => v.IncrementAsync(_target, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_Owner_IsProtected()
    {
        var result = await RemoveSut().ExecuteAsync(new RemoveRoleFromUserCommand(_actor, _target, _owner.Id));

        Assert.Equal(AuthorizationErrors.OwnerProtected, result.Error);
    }

    [Fact]
    public async Task GetMyRoles_DoesNotRequireIamPermission()
    {
        _memberships.Setup(m => m.GetRolesForUserAsync(_actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IdentityRoleRecord> { _manager });

        var result = await new GetMyRolesUseCase(_users.Object, _memberships.Object)
            .ExecuteAsync(new GetMyRolesQuery(_actor));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        _permissions.Verify(
            p => p.HasPermissionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetUserRoles_RequiresHierarchy()
    {
        _hierarchy.Setup(h => h.CanManageUserAsync(_actor, _target, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await new GetUserRolesUseCase(
                _permissions.Object, _hierarchy.Object, _users.Object, _memberships.Object)
            .ExecuteAsync(new GetUserRolesQuery(_actor, _target));

        Assert.Equal(AuthorizationErrors.HierarchyViolation, result.Error);
    }

    private AssignRoleToUserUseCase AssignSut() =>
        new(
            _permissions.Object,
            _hierarchy.Object,
            _users.Object,
            _roles.Object,
            _memberships.Object,
            _writer.Object,
            _versions.Object,
            _audit.Object,
            _uow.Object,
            _clock.Object);

    private RemoveRoleFromUserUseCase RemoveSut() =>
        new(
            _permissions.Object,
            _hierarchy.Object,
            _users.Object,
            _roles.Object,
            _memberships.Object,
            _writer.Object,
            _versions.Object,
            _audit.Object,
            _uow.Object,
            _clock.Object);
}
