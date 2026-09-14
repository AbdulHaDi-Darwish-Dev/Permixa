using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.EffectivePermissions;
using Permixa.Application.Authorization.Models;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authorization;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class EffectivePermissionServiceTests
{
    private readonly Mock<IIdentityUserReader> _userReader = new();
    private readonly Mock<IPermissionRepository> _permissionRepository = new();
    private readonly Mock<IUserPermissionOverrideRepository> _overrideRepository = new();
    private readonly Mock<IAuthorizationStateRepository> _stateRepository = new();
    private readonly Mock<IUserAuthorizationVersionStore> _versionStore = new();
    private readonly Mock<IRoleHierarchyReader> _hierarchyReader = new();
    private readonly Mock<IPermissionCache> _cache = new();
    private readonly EffectivePermissionService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleA = Guid.NewGuid();
    private readonly Guid _roleB = Guid.NewGuid();

    public EffectivePermissionServiceTests()
    {
        _sut = new EffectivePermissionService(
            _userReader.Object,
            _permissionRepository.Object,
            _overrideRepository.Object,
            _stateRepository.Object,
            _versionStore.Object,
            _hierarchyReader.Object,
            _cache.Object);

        _userReader.Setup(r => r.UserExistsAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _versionStore.Setup(v => v.GetAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        _stateRepository.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationState.Reconstitute(AuthorizationState.GlobalId, 7));
        _hierarchyReader.Setup(h => h.GetEffectiveUserLevelAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(20);
        _cache.Setup(c => c.GetAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationSnapshot?)null);
    }

    [Fact]
    public async Task RolePermission_ResolvesToAllow()
    {
        SetupRoles([_roleA]);
        var ordersRead = Permission.Create("Orders.Read");
        _permissionRepository
            .Setup(p => p.GetByRoleIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([ordersRead]);
        _overrideRepository
            .Setup(o => o.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _sut.GetAuthorizationSnapshotAsync(_userId);

        Assert.True(result.IsSuccess);
        Assert.Contains("Orders.Read", result.Value.Permissions);
    }

    [Fact]
    public async Task NoRolePermission_DefaultsToDeny()
    {
        SetupRoles([_roleA]);
        _permissionRepository
            .Setup(p => p.GetByRoleIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _overrideRepository
            .Setup(o => o.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _sut.GetAuthorizationSnapshotAsync(_userId);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Permissions);
    }

    [Fact]
    public async Task UserAllowOverride_AddsPermission()
    {
        SetupRoles([]);
        var export = Permission.Create("Reports.Export");
        _permissionRepository
            .Setup(p => p.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([export]);
        _overrideRepository
            .Setup(o => o.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([UserPermissionOverride.Create(_userId, export.Id, PermissionEffect.Allow)]);

        var result = await _sut.GetAuthorizationSnapshotAsync(_userId);

        Assert.Contains("Reports.Export", result.Value.Permissions);
    }

    [Fact]
    public async Task MultipleOverridePermissions_AreLoadedWithSingleBulkQuery()
    {
        SetupRoles([]);

        var permissions = Enumerable.Range(1, 25)
            .Select(i => Permission.Create($"Reports.Action{i}"))
            .ToArray();

        var overrides = permissions
            .Select(p => UserPermissionOverride.Create(_userId, p.Id, PermissionEffect.Allow))
            .ToArray();

        // Duplicate PermissionId in overrides must still yield one bulk call with distinct ids.
        var overridesWithDuplicate = overrides
            .Concat([UserPermissionOverride.Create(_userId, permissions[0].Id, PermissionEffect.Allow)])
            .ToArray();

        _overrideRepository
            .Setup(o => o.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overridesWithDuplicate);

        IReadOnlyCollection<Guid>? requestedIds = null;
        _permissionRepository
            .Setup(p => p.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, CancellationToken>((ids, _) => requestedIds = ids)
            .ReturnsAsync(permissions);

        var result = await _sut.GetAuthorizationSnapshotAsync(_userId);

        Assert.True(result.IsSuccess);
        Assert.Equal(25, result.Value.Permissions.Count);
        Assert.NotNull(requestedIds);
        Assert.Equal(25, requestedIds!.Count);
        Assert.Equal(25, requestedIds.Distinct().Count());

        _permissionRepository.Verify(
            p => p.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _permissionRepository.Verify(
            p => p.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OverrideAlreadyCoveredByRolePermissions_DoesNotCallGetByIds()
    {
        SetupRoles([_roleA]);
        var delete = Permission.Create("Orders.Delete");
        _permissionRepository
            .Setup(p => p.GetByRoleIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([delete]);
        _overrideRepository
            .Setup(o => o.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([UserPermissionOverride.Create(_userId, delete.Id, PermissionEffect.Deny)]);

        var result = await _sut.GetAuthorizationSnapshotAsync(_userId);

        Assert.DoesNotContain("Orders.Delete", result.Value.Permissions);
        _permissionRepository.Verify(
            p => p.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _permissionRepository.Verify(
            p => p.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UserDenyOverride_RemovesRolePermission()
    {
        SetupRoles([_roleA]);
        var delete = Permission.Create("Orders.Delete");
        _permissionRepository
            .Setup(p => p.GetByRoleIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([delete]);
        _overrideRepository
            .Setup(o => o.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([UserPermissionOverride.Create(_userId, delete.Id, PermissionEffect.Deny)]);

        var result = await _sut.GetAuthorizationSnapshotAsync(_userId);

        Assert.DoesNotContain("Orders.Delete", result.Value.Permissions);
    }

    [Fact]
    public async Task MultipleRolePermissions_AreUnioned_WithoutDuplicates()
    {
        SetupRoles([_roleA, _roleB]);
        var read = Permission.Create("Orders.Read");
        var write = Permission.Create("Orders.Write");
        _permissionRepository
            .Setup(p => p.GetByRoleIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([read, write, read]);
        _overrideRepository
            .Setup(o => o.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _sut.GetAuthorizationSnapshotAsync(_userId);

        Assert.Equal(2, result.Value.Permissions.Count);
        Assert.Contains("Orders.Read", result.Value.Permissions);
        Assert.Contains("Orders.Write", result.Value.Permissions);
    }

    [Fact]
    public async Task Snapshot_ContainsVersionsAndEffectiveLevel()
    {
        SetupRoles([]);
        _permissionRepository
            .Setup(p => p.GetByRoleIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _overrideRepository
            .Setup(o => o.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _sut.GetAuthorizationSnapshotAsync(_userId);

        Assert.Equal(3, result.Value.UserVersion);
        Assert.Equal(7, result.Value.RbacVersion);
        Assert.Equal(20, result.Value.EffectiveRoleLevel);
    }

    [Fact]
    public async Task ValidCacheHit_AvoidsFullResolution()
    {
        var cached = new AuthorizationSnapshot(
            3,
            7,
            20,
            new HashSet<string>(StringComparer.Ordinal) { "Cached.Permission" });

        _cache.Setup(c => c.GetAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(cached);

        var result = await _sut.GetAuthorizationSnapshotAsync(_userId);

        Assert.Same(cached, result.Value);
        _userReader.Verify(r => r.GetUserRoleIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _cache.Verify(c => c.SetAsync(It.IsAny<Guid>(), It.IsAny<AuthorizationSnapshot>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StaleUserVersion_ForcesRebuild()
    {
        var stale = new AuthorizationSnapshot(1, 7, 20, new HashSet<string>());
        _cache.Setup(c => c.GetAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(stale);
        SetupRoles([]);
        _permissionRepository
            .Setup(p => p.GetByRoleIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _overrideRepository
            .Setup(o => o.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _sut.GetAuthorizationSnapshotAsync(_userId);

        Assert.Equal(3, result.Value.UserVersion);
        _cache.Verify(
            c => c.SetAsync(_userId, It.Is<AuthorizationSnapshot>(s => s.UserVersion == 3 && s.RbacVersion == 7), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StaleRbacVersion_ForcesRebuild()
    {
        var stale = new AuthorizationSnapshot(3, 1, 20, new HashSet<string>());
        _cache.Setup(c => c.GetAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(stale);
        SetupRoles([]);
        _permissionRepository
            .Setup(p => p.GetByRoleIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _overrideRepository
            .Setup(o => o.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _sut.GetAuthorizationSnapshotAsync(_userId);

        Assert.Equal(7, result.Value.RbacVersion);
        _cache.Verify(c => c.SetAsync(_userId, It.IsAny<AuthorizationSnapshot>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetupRoles(IReadOnlyCollection<Guid> roleIds)
    {
        _userReader
            .Setup(r => r.GetUserRoleIdsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roleIds);
    }
}
