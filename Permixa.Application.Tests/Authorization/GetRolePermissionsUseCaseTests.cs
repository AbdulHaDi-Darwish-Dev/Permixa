using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.RolePermissions.Get;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authorization;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class GetRolePermissionsUseCaseTests
{
    private readonly Mock<IEffectivePermissionService> _effectivePermissions = new();
    private readonly Mock<IAuthorizationHierarchyService> _hierarchy = new();
    private readonly Mock<IIdentityRoleReader> _roleReader = new();
    private readonly Mock<IPermissionRepository> _permissionRepository = new();
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    public GetRolePermissionsUseCaseTests()
    {
        GrantRead(true);
        _roleReader.Setup(r => r.RoleExistsAsync(_roleId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _hierarchy.Setup(h => h.CanManageRoleAsync(_actorId, _roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task GetRolePermissions_ManageableRole_ReturnsPermissionDtos()
    {
        var first = Permission.Create("Orders.Read");
        var second = Permission.Create("Orders.Write");
        _permissionRepository
            .Setup(p => p.GetByRoleIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == _roleId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([second, first]);

        var result = await CreateSut().ExecuteAsync(new GetRolePermissionsQuery(_actorId, _roleId));

        Assert.True(result.IsSuccess);
        Assert.Equal(["Orders.Read", "Orders.Write"], result.Value.Select(p => p.Name).ToArray());
        _permissionRepository.Verify(
            p => p.GetByRoleIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _permissionRepository.Verify(
            p => p.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetRolePermissions_MissingRead_IsForbidden()
    {
        GrantRead(false);

        var result = await CreateSut().ExecuteAsync(new GetRolePermissionsQuery(_actorId, _roleId));

        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
        _permissionRepository.Verify(
            p => p.GetByRoleIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetRolePermissions_HierarchyDenied_IsForbidden()
    {
        _hierarchy.Setup(h => h.CanManageRoleAsync(_actorId, _roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateSut().ExecuteAsync(new GetRolePermissionsQuery(_actorId, _roleId));

        Assert.Equal(AuthorizationErrors.HierarchyViolation, result.Error);
    }

    [Fact]
    public async Task GetRolePermissions_MissingRole_IsNotFound()
    {
        _roleReader.Setup(r => r.RoleExistsAsync(_roleId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateSut().ExecuteAsync(new GetRolePermissionsQuery(_actorId, _roleId));

        Assert.Equal(AuthorizationErrors.RoleNotFound, result.Error);
        _hierarchy.Verify(
            h => h.CanManageRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetRolePermissions_ManyPermissions_UsesOneSetBasedLookup()
    {
        var many = Enumerable.Range(1, 40)
            .Select(i => Permission.Create($"Orders.Action{i}"))
            .ToArray();
        _permissionRepository
            .Setup(p => p.GetByRoleIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(many);

        var result = await CreateSut().ExecuteAsync(new GetRolePermissionsQuery(_actorId, _roleId));

        Assert.True(result.IsSuccess);
        Assert.Equal(40, result.Value.Count);
        _permissionRepository.Verify(
            p => p.GetByRoleIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void GrantRead(bool allowed) =>
        _effectivePermissions
            .Setup(e => e.HasPermissionAsync(_actorId, IamPermissions.Permissions.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(allowed));

    private GetRolePermissionsUseCase CreateSut() =>
        new(
            _effectivePermissions.Object,
            _hierarchy.Object,
            _roleReader.Object,
            _permissionRepository.Object);
}
