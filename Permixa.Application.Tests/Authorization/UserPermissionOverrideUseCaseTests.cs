using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.UserPermissionOverrides.Remove;
using Permixa.Application.Authorization.UserPermissionOverrides.Set;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authorization;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class UserPermissionOverrideUseCaseTests
{
    private readonly Mock<IIdentityUserReader> _userReader = new();
    private readonly Mock<IPermissionRepository> _permissionRepository = new();
    private readonly Mock<IUserPermissionOverrideRepository> _overrideRepository = new();
    private readonly Mock<IAuthorizationHierarchyService> _hierarchy = new();
    private readonly Mock<IEffectivePermissionService> _effectivePermissions = new();
    private readonly Mock<IUserAuthorizationVersionStore> _versionStore = new();
    private readonly Mock<IIamAuditSink> _audit = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IClock> _clock = new();

    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Guid _targetId = Guid.NewGuid();
    private readonly Permission _permission = Permission.Create("Orders.Delete");

    public UserPermissionOverrideUseCaseTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc));
        _effectivePermissions
            .Setup(e => e.HasPermissionAsync(
                _actorId,
                IamPermissions.UserPermissionOverrides.Manage,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));
        _userReader.Setup(u => u.UserExistsAsync(_targetId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _permissionRepository.Setup(p => p.GetByIdAsync(_permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_permission);
        _hierarchy.Setup(h => h.CanManageUserAsync(_actorId, _targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task SetAllowOverride_CreatesAndIncrementsTargetUserVersion()
    {
        _overrideRepository
            .Setup(o => o.GetAsync(_targetId, _permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPermissionOverride?)null);

        var sut = CreateSetUseCase();
        var result = await sut.ExecuteAsync(
            new SetUserPermissionOverrideCommand(_actorId, _targetId, _permission.Id, PermissionEffect.Allow));

        Assert.True(result.IsSuccess);
        Assert.Equal(PermissionEffect.Allow, result.Value.Effect);
        _overrideRepository.Verify(
            o => o.AddAsync(It.IsAny<UserPermissionOverride>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _versionStore.Verify(v => v.IncrementAsync(_targetId, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetDenyOverride_CreatesDeny()
    {
        _overrideRepository
            .Setup(o => o.GetAsync(_targetId, _permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPermissionOverride?)null);

        var sut = CreateSetUseCase();
        var result = await sut.ExecuteAsync(
            new SetUserPermissionOverrideCommand(_actorId, _targetId, _permission.Id, PermissionEffect.Deny));

        Assert.Equal(PermissionEffect.Deny, result.Value.Effect);
    }

    [Fact]
    public async Task ChangeAllowToDeny_IncrementsVersionOnce()
    {
        var existing = UserPermissionOverride.Create(_targetId, _permission.Id, PermissionEffect.Allow);
        _overrideRepository
            .Setup(o => o.GetAsync(_targetId, _permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateSetUseCase();
        var result = await sut.ExecuteAsync(
            new SetUserPermissionOverrideCommand(_actorId, _targetId, _permission.Id, PermissionEffect.Deny));

        Assert.True(result.IsSuccess);
        Assert.Equal(PermissionEffect.Deny, existing.Effect);
        _versionStore.Verify(v => v.IncrementAsync(_targetId, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeDenyToAllow_IncrementsVersionOnce()
    {
        var existing = UserPermissionOverride.Create(_targetId, _permission.Id, PermissionEffect.Deny);
        _overrideRepository
            .Setup(o => o.GetAsync(_targetId, _permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateSetUseCase();
        var result = await sut.ExecuteAsync(
            new SetUserPermissionOverrideCommand(_actorId, _targetId, _permission.Id, PermissionEffect.Allow));

        Assert.Equal(PermissionEffect.Allow, existing.Effect);
        _versionStore.Verify(v => v.IncrementAsync(_targetId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SameRequestedState_NoMutation_NoVersionIncrement()
    {
        var existing = UserPermissionOverride.Create(_targetId, _permission.Id, PermissionEffect.Allow);
        _overrideRepository
            .Setup(o => o.GetAsync(_targetId, _permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateSetUseCase();
        var result = await sut.ExecuteAsync(
            new SetUserPermissionOverrideCommand(_actorId, _targetId, _permission.Id, PermissionEffect.Allow));

        Assert.Equal(AuthorizationErrors.OverrideUnchanged, result.Error);
        _versionStore.Verify(v => v.IncrementAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveOverride_IncrementsVersionOnce()
    {
        var existing = UserPermissionOverride.Create(_targetId, _permission.Id, PermissionEffect.Deny);
        _overrideRepository
            .Setup(o => o.GetAsync(_targetId, _permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateRemoveUseCase();
        var result = await sut.ExecuteAsync(
            new RemoveUserPermissionOverrideCommand(_actorId, _targetId, _permission.Id));

        Assert.True(result.IsSuccess);
        _overrideRepository.Verify(
            o => o.RemoveAsync(_targetId, _permission.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        _versionStore.Verify(v => v.IncrementAsync(_targetId, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveMissingOverride_DoesNotIncrementVersion()
    {
        _overrideRepository
            .Setup(o => o.GetAsync(_targetId, _permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPermissionOverride?)null);

        var sut = CreateRemoveUseCase();
        var result = await sut.ExecuteAsync(
            new RemoveUserPermissionOverrideCommand(_actorId, _targetId, _permission.Id));

        Assert.Equal(AuthorizationErrors.OverrideNotFound, result.Error);
        _versionStore.Verify(v => v.IncrementAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SelfOverride_IsDenied()
    {
        var sut = CreateSetUseCase();
        var result = await sut.ExecuteAsync(
            new SetUserPermissionOverrideCommand(_actorId, _actorId, _permission.Id, PermissionEffect.Allow));

        Assert.Equal(AuthorizationErrors.CannotManageSelf, result.Error);
    }

    private SetUserPermissionOverrideUseCase CreateSetUseCase() =>
        new(
            _userReader.Object,
            _permissionRepository.Object,
            _overrideRepository.Object,
            _hierarchy.Object,
            _effectivePermissions.Object,
            _versionStore.Object,
            _audit.Object,
            _unitOfWork.Object,
            _clock.Object);

    private RemoveUserPermissionOverrideUseCase CreateRemoveUseCase() =>
        new(
            _userReader.Object,
            _permissionRepository.Object,
            _overrideRepository.Object,
            _hierarchy.Object,
            _effectivePermissions.Object,
            _versionStore.Object,
            _audit.Object,
            _unitOfWork.Object,
            _clock.Object);
}
