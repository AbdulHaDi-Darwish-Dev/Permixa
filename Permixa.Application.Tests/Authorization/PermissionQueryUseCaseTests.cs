using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Permissions.Get;
using Permixa.Application.Common.Results;
using Permixa.Domain.Authorization;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class PermissionQueryUseCaseTests
{
    private readonly Mock<IEffectivePermissionService> _effectivePermissions = new();
    private readonly Mock<IPermissionRepository> _repo = new();
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Permission _permission = Permission.Create("Orders.Read", "Read orders");

    public PermissionQueryUseCaseTests()
    {
        GrantRead(true);
        _repo.Setup(r => r.GetByIdAsync(_permission.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_permission);
        _repo.Setup(r => r.GetByNameAsync("Orders.Read", It.IsAny<CancellationToken>())).ReturnsAsync(_permission);
        _repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([_permission]);
    }

    [Fact]
    public async Task GetPermissionById_ReadGranted_ReturnsDto()
    {
        var result = await CreateById().ExecuteAsync(new GetPermissionByIdQuery(_actorId, _permission.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(_permission.Id, result.Value.Id);
        Assert.Equal("Orders.Read", result.Value.Name);
    }

    [Fact]
    public async Task GetPermissionById_MissingRead_IsForbidden()
    {
        GrantRead(false);

        var result = await CreateById().ExecuteAsync(new GetPermissionByIdQuery(_actorId, _permission.Id));

        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
        _repo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPermissions_ReadGranted_ReturnsCatalog()
    {
        var result = await CreateList().ExecuteAsync(new GetPermissionsQuery(_actorId));

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, p => p.Name == "Orders.Read");
    }

    [Fact]
    public async Task GetPermissions_MissingRead_IsForbidden()
    {
        GrantRead(false);

        var result = await CreateList().ExecuteAsync(new GetPermissionsQuery(_actorId));

        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
        _repo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPermissionByName_ExistingName_ReturnsDto()
    {
        var result = await CreateByName().ExecuteAsync(new GetPermissionByNameQuery(_actorId, " Orders.Read "));

        Assert.True(result.IsSuccess);
        Assert.Equal(_permission.Id, result.Value.Id);
    }

    [Fact]
    public async Task GetPermissionByName_MissingName_IsNotFound()
    {
        _repo.Setup(r => r.GetByNameAsync("Orders.Missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission?)null);

        var result = await CreateByName().ExecuteAsync(new GetPermissionByNameQuery(_actorId, "Orders.Missing"));

        Assert.Equal(AuthorizationErrors.PermissionNotFound, result.Error);
    }

    [Fact]
    public async Task GetPermissionByName_MissingRead_IsForbidden()
    {
        GrantRead(false);

        var result = await CreateByName().ExecuteAsync(new GetPermissionByNameQuery(_actorId, "Orders.Read"));

        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
        _repo.Verify(r => r.GetByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void GrantRead(bool allowed) =>
        _effectivePermissions
            .Setup(e => e.HasPermissionAsync(_actorId, IamPermissions.Permissions.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(allowed));

    private GetPermissionByIdUseCase CreateById() =>
        new(_effectivePermissions.Object, _repo.Object);

    private GetPermissionsUseCase CreateList() =>
        new(_effectivePermissions.Object, _repo.Object);

    private GetPermissionByNameUseCase CreateByName() =>
        new(_effectivePermissions.Object, _repo.Object);
}
