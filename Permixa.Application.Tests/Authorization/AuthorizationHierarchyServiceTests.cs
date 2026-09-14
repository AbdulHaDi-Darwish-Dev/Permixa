using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Hierarchy;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class AuthorizationHierarchyServiceTests
{
    private readonly Mock<IRoleHierarchyReader> _reader = new();
    private readonly AuthorizationHierarchyService _sut;

    public AuthorizationHierarchyServiceTests()
    {
        _sut = new AuthorizationHierarchyService(_reader.Object);
    }

    [Fact]
    public async Task Actor20_CanManage_Target30()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        _reader.Setup(r => r.GetEffectiveUserLevelAsync(actor, It.IsAny<CancellationToken>())).ReturnsAsync(20);
        _reader.Setup(r => r.GetEffectiveUserLevelAsync(target, It.IsAny<CancellationToken>())).ReturnsAsync(30);

        Assert.True(await _sut.CanManageUserAsync(actor, target));
    }

    [Fact]
    public async Task Actor30_CannotManage_Target20()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        _reader.Setup(r => r.GetEffectiveUserLevelAsync(actor, It.IsAny<CancellationToken>())).ReturnsAsync(30);
        _reader.Setup(r => r.GetEffectiveUserLevelAsync(target, It.IsAny<CancellationToken>())).ReturnsAsync(20);

        Assert.False(await _sut.CanManageUserAsync(actor, target));
    }

    [Fact]
    public async Task Actor30_CannotManage_Target30()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        _reader.Setup(r => r.GetEffectiveUserLevelAsync(actor, It.IsAny<CancellationToken>())).ReturnsAsync(30);
        _reader.Setup(r => r.GetEffectiveUserLevelAsync(target, It.IsAny<CancellationToken>())).ReturnsAsync(30);

        Assert.False(await _sut.CanManageUserAsync(actor, target));
    }

    [Fact]
    public async Task NoActorLevel_Denies()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        _reader.Setup(r => r.GetEffectiveUserLevelAsync(actor, It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        _reader.Setup(r => r.GetEffectiveUserLevelAsync(target, It.IsAny<CancellationToken>())).ReturnsAsync(40);

        Assert.False(await _sut.CanManageUserAsync(actor, target));
    }

    [Fact]
    public async Task TargetWithoutLevel_IsManageableByActorWithLevel()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        _reader.Setup(r => r.GetEffectiveUserLevelAsync(actor, It.IsAny<CancellationToken>())).ReturnsAsync(20);
        _reader.Setup(r => r.GetEffectiveUserLevelAsync(target, It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);

        Assert.True(await _sut.CanManageUserAsync(actor, target));
    }

    [Fact]
    public async Task SelfManagement_IsDenied()
    {
        var userId = Guid.NewGuid();
        _reader.Setup(r => r.GetEffectiveUserLevelAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(10);

        Assert.False(await _sut.CanManageUserAsync(userId, userId));
    }

    [Fact]
    public async Task CanAssignRole_RequiresActorHigherThanRole()
    {
        var actor = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _reader.Setup(r => r.GetEffectiveUserLevelAsync(actor, It.IsAny<CancellationToken>())).ReturnsAsync(20);
        _reader.Setup(r => r.GetRoleLevelAsync(roleId, It.IsAny<CancellationToken>())).ReturnsAsync(10);

        Assert.False(await _sut.CanAssignRoleAsync(actor, roleId));

        _reader.Setup(r => r.GetRoleLevelAsync(roleId, It.IsAny<CancellationToken>())).ReturnsAsync(40);
        Assert.True(await _sut.CanAssignRoleAsync(actor, roleId));
    }

    [Fact]
    public async Task CanCreateOrChangeRoleToLevel_BlocksEqualOrHigherAuthority()
    {
        var actor = Guid.NewGuid();
        _reader.Setup(r => r.GetEffectiveUserLevelAsync(actor, It.IsAny<CancellationToken>())).ReturnsAsync(20);

        Assert.False(await _sut.CanCreateOrChangeRoleToLevelAsync(actor, 20));
        Assert.False(await _sut.CanCreateOrChangeRoleToLevelAsync(actor, 10));
        Assert.True(await _sut.CanCreateOrChangeRoleToLevelAsync(actor, 30));
    }
}
