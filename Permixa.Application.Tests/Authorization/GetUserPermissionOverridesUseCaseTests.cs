using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.UserPermissionOverrides.Get;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authorization;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class GetUserPermissionOverridesUseCaseTests
{
    private readonly Mock<IEffectivePermissionService> _effectivePermissions = new();
    private readonly Mock<IAuthorizationHierarchyService> _hierarchy = new();
    private readonly Mock<IIdentityUserReader> _userReader = new();
    private readonly Mock<IUserPermissionOverrideRepository> _overrideRepository = new();
    private readonly Mock<IPermissionRepository> _permissionRepository = new();
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Guid _targetId = Guid.NewGuid();

    public GetUserPermissionOverridesUseCaseTests()
    {
        GrantManage(true);
        _userReader.Setup(u => u.UserExistsAsync(_targetId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _hierarchy.Setup(h => h.CanManageUserAsync(_actorId, _targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task GetOverrides_ManageableTarget_ReturnsNamesAndEffects()
    {
        var permission = Permission.Create("Orders.Delete");
        var permissionOverride = UserPermissionOverride.Create(_targetId, permission.Id, PermissionEffect.Deny);
        _overrideRepository
            .Setup(o => o.GetByUserIdAsync(_targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([permissionOverride]);
        _permissionRepository
            .Setup(p => p.GetByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == permission.Id),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([permission]);

        var result = await CreateSut().ExecuteAsync(new GetUserPermissionOverridesQuery(_actorId, _targetId));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value);
        Assert.Equal(permission.Id, item.PermissionId);
        Assert.Equal("Orders.Delete", item.PermissionName);
        Assert.Equal(PermissionEffect.Deny, item.Effect);
    }

    [Fact]
    public async Task GetOverrides_MissingManage_IsForbidden()
    {
        GrantManage(false);

        var result = await CreateSut().ExecuteAsync(new GetUserPermissionOverridesQuery(_actorId, _targetId));

        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
        _overrideRepository.Verify(
            o => o.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOverrides_SelfTarget_IsDenied()
    {
        var result = await CreateSut().ExecuteAsync(new GetUserPermissionOverridesQuery(_actorId, _actorId));

        Assert.Equal(AuthorizationErrors.CannotManageSelf, result.Error);
    }

    [Fact]
    public async Task GetOverrides_HierarchyDenied_IsForbidden()
    {
        _hierarchy.Setup(h => h.CanManageUserAsync(_actorId, _targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateSut().ExecuteAsync(new GetUserPermissionOverridesQuery(_actorId, _targetId));

        Assert.Equal(AuthorizationErrors.HierarchyViolation, result.Error);
    }

    [Fact]
    public async Task GetOverrides_None_ReturnsEmptyList()
    {
        _overrideRepository
            .Setup(o => o.GetByUserIdAsync(_targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateSut().ExecuteAsync(new GetUserPermissionOverridesQuery(_actorId, _targetId));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        _permissionRepository.Verify(
            p => p.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOverrides_Many_UsesOnePermissionLookup()
    {
        var permissions = Enumerable.Range(1, 25)
            .Select(i => Permission.Create($"Orders.Action{i}"))
            .ToArray();
        var overrides = permissions
            .Select(p => UserPermissionOverride.Create(_targetId, p.Id, PermissionEffect.Allow))
            .ToArray();
        _overrideRepository
            .Setup(o => o.GetByUserIdAsync(_targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overrides);
        _permissionRepository
            .Setup(p => p.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        var result = await CreateSut().ExecuteAsync(new GetUserPermissionOverridesQuery(_actorId, _targetId));

        Assert.True(result.IsSuccess);
        Assert.Equal(25, result.Value.Count);
        _permissionRepository.Verify(
            p => p.GetByIdsAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 25), It.IsAny<CancellationToken>()),
            Times.Once);
        _permissionRepository.Verify(
            p => p.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void GrantManage(bool allowed) =>
        _effectivePermissions
            .Setup(e => e.HasPermissionAsync(
                _actorId,
                IamPermissions.UserPermissionOverrides.Manage,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(allowed));

    private GetUserPermissionOverridesUseCase CreateSut() =>
        new(
            _effectivePermissions.Object,
            _hierarchy.Object,
            _userReader.Object,
            _overrideRepository.Object,
            _permissionRepository.Object);
}
