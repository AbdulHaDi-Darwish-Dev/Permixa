using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.EffectivePermissions;
using Permixa.Application.Authorization.Models;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class EffectivePermissionsQueryUseCaseTests
{
    private readonly Mock<IEffectivePermissionService> _effectivePermissions = new();
    private readonly Mock<IAuthorizationHierarchyService> _hierarchy = new();
    private readonly Mock<IIdentityUserReader> _userReader = new();
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Guid _targetId = Guid.NewGuid();

    public EffectivePermissionsQueryUseCaseTests()
    {
        GrantRead(true);
        _userReader.Setup(u => u.UserExistsAsync(_targetId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _hierarchy.Setup(h => h.CanManageUserAsync(_actorId, _targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task AdminQuery_ReturnsDeterministicNames_WithoutVersions()
    {
        SetupSnapshot(_targetId, ["Orders.Write", "Orders.Read"]);

        var result = await CreateAdmin().ExecuteAsync(new GetEffectivePermissionsQuery(_actorId, _targetId));

        Assert.True(result.IsSuccess);
        Assert.Equal(["Orders.Read", "Orders.Write"], result.Value.Permissions);
    }

    [Fact]
    public async Task AdminQuery_UserDenyWinsOverRoleGrant()
    {
        SetupSnapshot(_targetId, ["Orders.Write"]);

        var result = await CreateAdmin().ExecuteAsync(new GetEffectivePermissionsQuery(_actorId, _targetId));

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("Orders.Approve", result.Value.Permissions);
        Assert.Contains("Orders.Write", result.Value.Permissions);
    }

    [Fact]
    public async Task AdminQuery_MissingRead_IsForbidden()
    {
        GrantRead(false);

        var result = await CreateAdmin().ExecuteAsync(new GetEffectivePermissionsQuery(_actorId, _targetId));

        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
        _effectivePermissions.Verify(
            e => e.GetAuthorizationSnapshotAsync(_targetId, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AdminQuery_HierarchyDenied_IsForbidden()
    {
        _hierarchy.Setup(h => h.CanManageUserAsync(_actorId, _targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateAdmin().ExecuteAsync(new GetEffectivePermissionsQuery(_actorId, _targetId));

        Assert.Equal(AuthorizationErrors.HierarchyViolation, result.Error);
    }

    [Fact]
    public async Task SelfQuery_SucceedsWithoutIamRead()
    {
        SetupSnapshot(_actorId, ["Orders.Read"]);

        var result = await CreateSelf().ExecuteAsync(new GetMyEffectivePermissionsQuery(_actorId));

        Assert.True(result.IsSuccess);
        Assert.Equal(["Orders.Read"], result.Value.Permissions);
        _effectivePermissions.Verify(
            e => e.HasPermissionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void GrantRead(bool allowed) =>
        _effectivePermissions
            .Setup(e => e.HasPermissionAsync(_actorId, IamPermissions.Permissions.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(allowed));

    private void SetupSnapshot(Guid userId, IReadOnlyList<string> names)
    {
        var snapshot = new AuthorizationSnapshot(
            UserVersion: 3,
            RbacVersion: 7,
            EffectiveRoleLevel: 20,
            Permissions: names.ToHashSet(StringComparer.Ordinal));

        _effectivePermissions
            .Setup(e => e.GetAuthorizationSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(snapshot));
    }

    private GetEffectivePermissionsUseCase CreateAdmin() =>
        new(_effectivePermissions.Object, _hierarchy.Object, _userReader.Object);

    private GetMyEffectivePermissionsUseCase CreateSelf() =>
        new(_effectivePermissions.Object);
}
