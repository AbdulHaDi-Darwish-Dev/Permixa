using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Users.Create;
using Permixa.Application.Authorization.Users.Disable;
using Permixa.Application.Authorization.Users.Get;
using Permixa.Application.Authorization.Users.Lock;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Paging;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authorization;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class UserAdministrationUseCaseTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IEffectivePermissionService> _permissions = new();
    private readonly Mock<IAuthorizationHierarchyService> _hierarchy = new();
    private readonly Mock<IIdentityUserReader> _users = new();
    private readonly Mock<IIdentityUserWriter> _writer = new();
    private readonly Mock<IIdentityUserCreator> _creator = new();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IIamAuditSink> _audit = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IRefreshTokenRepository> _refresh = new();
    private readonly Mock<IRoleHierarchyReader> _levels = new();

    private readonly Guid _actor = Guid.NewGuid();
    private readonly Guid _target = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();

    public UserAdministrationUseCaseTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _uow.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((action, ct) => action(ct));
        Grant(IamPermissions.Users.Create, true);
        Grant(IamPermissions.Users.Read, true);
        Grant(IamPermissions.Users.Lock, true);
        Grant(IamPermissions.Users.Disable, true);
        _hierarchy.Setup(h => h.CanManageUserAsync(_actor, _target, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.UserExistsAsync(_target, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.HasRoleNameAsync(_target, PermixaRoles.Owner, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _users.Setup(u => u.HasRoleNameAsync(_owner, PermixaRoles.Owner, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    [Fact]
    public async Task AdminCreateUser_MissingPermission_IsForbidden()
    {
        Grant(IamPermissions.Users.Create, false);
        var result = await CreateUserSut().ExecuteAsync(
            new AdminCreateUserCommand(_actor, "a@test.local", "alice", "Passw0rd!"));
        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
        _creator.Verify(c => c.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdminCreateUser_DuplicateEmail_UsesExistingConflict()
    {
        _creator.Setup(c => c.CreateAsync("alice", "a@test.local", "Passw0rd!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityUserCreationResult.Failed(IdentityUserCreationFailure.DuplicateEmail));

        var result = await CreateUserSut().ExecuteAsync(
            new AdminCreateUserCommand(_actor, "a@test.local", "alice", "Passw0rd!"));

        Assert.Equal(AuthenticationErrors.EmailAlreadyExists, result.Error);
    }

    [Fact]
    public async Task AdminCreateUser_Success_DoesNotAssignRoles()
    {
        var id = Guid.NewGuid();
        _creator.Setup(c => c.CreateAsync("alice", "a@test.local", "Passw0rd!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityUserCreationResult.Success(id, emailConfirmed: false));
        _users.Setup(u => u.GetIamUserByIdAsync(id, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleUser(id, "alice", "a@test.local"));

        var result = await CreateUserSut().ExecuteAsync(
            new AdminCreateUserCommand(_actor, "a@test.local", "alice", "Passw0rd!"));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.EmailConfirmed);
        Assert.False(result.Value.IsDisabled);
        Assert.Null(result.Value.EffectiveRoleLevel);
    }

    [Fact]
    public async Task GetUserById_MissingPermission_IsForbidden()
    {
        Grant(IamPermissions.Users.Read, false);
        var result = await GetByIdSut().ExecuteAsync(new GetUserByIdQuery(_actor, _target));
        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
        _users.Verify(
            u => u.GetIamUserByIdAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetUserByEmail_ResolvesThenEnforcesHierarchy()
    {
        _users.Setup(u => u.GetIamUserByEmailAsync("peer@test.local", Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleUser(_target, "peer", "peer@test.local", 10));
        _hierarchy.Setup(h => h.CanManageUserAsync(_actor, _target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await new GetUserByEmailUseCase(
                _permissions.Object, _hierarchy.Object, _users.Object, _clock.Object)
            .ExecuteAsync(new GetUserByEmailQuery(_actor, "peer@test.local"));

        Assert.Equal(AuthorizationErrors.HierarchyViolation, result.Error);
    }

    [Fact]
    public async Task GetUserById_Peer_IsHierarchyViolation()
    {
        _hierarchy.Setup(h => h.CanManageUserAsync(_actor, _target, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _users.Setup(u => u.GetIamUserByIdAsync(_target, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleUser(_target, "peer", "peer@test.local", 10));

        var result = await GetByIdSut().ExecuteAsync(new GetUserByIdQuery(_actor, _target));
        Assert.Equal(AuthorizationErrors.HierarchyViolation, result.Error);
    }

    [Fact]
    public async Task GetUserById_Missing_IsNotFound()
    {
        _users.Setup(u => u.GetIamUserByIdAsync(_target, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUserIamRecord?)null);

        var result = await GetByIdSut().ExecuteAsync(new GetUserByIdQuery(_actor, _target));
        Assert.Equal(AuthorizationErrors.UserNotFound, result.Error);
    }

    [Fact]
    public async Task GetUsers_InvalidPage_IsValidation()
    {
        var result = await GetUsersSut().ExecuteAsync(
            new GetUsersQuery(_actor, new PageRequest(0, 20)));
        Assert.Equal(AuthorizationErrors.InvalidPaging, result.Error);
    }

    [Fact]
    public async Task GetUsers_ActorWithoutAuthority_ReturnsEmpty()
    {
        _levels.Setup(l => l.GetEffectiveUserLevelAsync(_actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);

        var result = await GetUsersSut().ExecuteAsync(new GetUsersQuery(_actor, new PageRequest()));
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        _users.Verify(
            u => u.SearchManageableUsersAsync(It.IsAny<IdentityUserSearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LockUser_PastTime_IsValidation()
    {
        var result = await LockSut().ExecuteAsync(new LockUserCommand(_actor, _target, Now));
        Assert.Equal(AuthorizationErrors.InvalidLockoutEnd, result.Error);
        _writer.Verify(
            w => w.SetLockoutAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LockUser_Self_IsDenied()
    {
        var result = await LockSut().ExecuteAsync(new LockUserCommand(_actor, _actor, Now.AddHours(1)));
        Assert.Equal(AuthorizationErrors.CannotManageSelf, result.Error);
    }

    [Fact]
    public async Task LockUser_Owner_IsProtected()
    {
        _users.Setup(u => u.UserExistsAsync(_owner, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var result = await LockSut().ExecuteAsync(new LockUserCommand(_actor, _owner, Now.AddHours(1)));
        Assert.Equal(AuthorizationErrors.OwnerProtected, result.Error);
    }

    [Fact]
    public async Task UnlockUser_AlreadyUnlocked_IsIdempotent()
    {
        _writer.Setup(w => w.UnlockAsync(_target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityLockMutation.Unchanged);

        var result = await UnlockSut().ExecuteAsync(new UnlockUserCommand(_actor, _target));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DisableUser_AlreadyDisabled_DoesNotRevokeAgain()
    {
        _users.Setup(u => u.GetAccountStateAsync(_target, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityAccountState(_target, true, false));

        var result = await DisableSut().ExecuteAsync(new DisableUserCommand(_actor, _target));
        Assert.True(result.IsSuccess);
        _writer.Verify(w => w.SetDisabledAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _refresh.Verify(
            r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DisableUser_RealChange_RevokesRefreshTokensOnce()
    {
        _users.Setup(u => u.GetAccountStateAsync(_target, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityAccountState(_target, false, false));
        _writer.Setup(w => w.SetDisabledAsync(_target, true, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await DisableSut().ExecuteAsync(new DisableUserCommand(_actor, _target));

        Assert.True(result.IsSuccess);
        _refresh.Verify(r => r.RevokeAllForUserAsync(_target, Now, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnableUser_AlreadyEnabled_IsIdempotent()
    {
        _users.Setup(u => u.GetAccountStateAsync(_target, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityAccountState(_target, false, false));

        var result = await EnableSut().ExecuteAsync(new EnableUserCommand(_actor, _target));
        Assert.True(result.IsSuccess);
        _writer.Verify(w => w.SetDisabledAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetUserIamDetails_Self_IsDenied()
    {
        var result = await new GetUserIamDetailsUseCase(
                _permissions.Object,
                _hierarchy.Object,
                _users.Object,
                Mock.Of<IIdentityUserRoleReader>(),
                Mock.Of<IUserPermissionOverrideRepository>(),
                Mock.Of<IPermissionRepository>(),
                _clock.Object)
            .ExecuteAsync(new GetUserIamDetailsQuery(_actor, _actor));

        Assert.Equal(AuthorizationErrors.CannotManageSelf, result.Error);
    }

    private void Grant(string permission, bool allowed) =>
        _permissions.Setup(p => p.HasPermissionAsync(_actor, permission, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(allowed));

    private static IdentityUserIamRecord SampleUser(
        Guid id,
        string name,
        string email,
        int? level = null) =>
        new(id, name, email, false, false, false, null, level, false, null);

    private AdminCreateUserUseCase CreateUserSut() =>
        new(_permissions.Object, _creator.Object, _users.Object, _audit.Object, _uow.Object, _clock.Object);

    private GetUserByIdUseCase GetByIdSut() =>
        new(_permissions.Object, _hierarchy.Object, _users.Object, _clock.Object);

    private GetUsersUseCase GetUsersSut() =>
        new(_permissions.Object, _levels.Object, _users.Object, _clock.Object);

    private LockUserUseCase LockSut() =>
        new(_permissions.Object, _hierarchy.Object, _users.Object, _writer.Object, _audit.Object, _uow.Object, _clock.Object);

    private UnlockUserUseCase UnlockSut() =>
        new(_permissions.Object, _hierarchy.Object, _users.Object, _writer.Object, _audit.Object, _uow.Object, _clock.Object);

    private DisableUserUseCase DisableSut() =>
        new(_permissions.Object, _hierarchy.Object, _users.Object, _writer.Object, _refresh.Object, _audit.Object, _uow.Object, _clock.Object);

    private EnableUserUseCase EnableSut() =>
        new(_permissions.Object, _hierarchy.Object, _users.Object, _writer.Object, _audit.Object, _uow.Object, _clock.Object);
}
