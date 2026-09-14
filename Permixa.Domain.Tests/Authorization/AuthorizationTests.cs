using Permixa.Domain.Authorization;

namespace Permixa.Domain.Tests.Authorization;

public sealed class PermissionEffectTests
{
    [Fact]
    public void PermissionEffect_SupportsAllowAndDeny()
    {
        Assert.Equal(1, (int)PermissionEffect.Allow);
        Assert.Equal(2, (int)PermissionEffect.Deny);
        Assert.True(Enum.IsDefined(PermissionEffect.Allow));
        Assert.True(Enum.IsDefined(PermissionEffect.Deny));
        Assert.False(Enum.IsDefined(typeof(PermissionEffect), 0));
    }
}

public sealed class AuthorizationStateTests
{
    [Fact]
    public void CreateInitial_StartsAtInitialRbacVersion()
    {
        var state = AuthorizationState.CreateInitial();

        Assert.Equal(AuthorizationState.InitialRbacVersion, state.RbacVersion);
        Assert.Equal(AuthorizationState.GlobalId, state.Id);
    }

    [Fact]
    public void IncrementRbacVersion_IncrementsSequentially()
    {
        var state = AuthorizationState.CreateInitial();

        var first = state.IncrementRbacVersion();
        var second = state.IncrementRbacVersion();

        Assert.Equal(AuthorizationState.InitialRbacVersion + 1, first);
        Assert.Equal(AuthorizationState.InitialRbacVersion + 2, second);
        Assert.Equal(AuthorizationState.InitialRbacVersion + 2, state.RbacVersion);
    }
}

public sealed class PermissionAuthorizationResolverTests
{
    [Fact]
    public void RoleGrants_NoOverride_Allows()
    {
        Assert.True(PermissionAuthorizationResolver.IsAllowed(
            roleGrantsPermission: true,
            userOverride: null));
    }

    [Fact]
    public void RoleGrants_UserDenyOverride_Denies()
    {
        Assert.False(PermissionAuthorizationResolver.IsAllowed(
            roleGrantsPermission: true,
            userOverride: PermissionEffect.Deny));
    }

    [Fact]
    public void RoleDoesNotGrant_UserAllowOverride_Allows()
    {
        Assert.True(PermissionAuthorizationResolver.IsAllowed(
            roleGrantsPermission: false,
            userOverride: PermissionEffect.Allow));
    }

    [Fact]
    public void RoleDoesNotGrant_NoOverride_Denies()
    {
        Assert.False(PermissionAuthorizationResolver.IsAllowed(
            roleGrantsPermission: false,
            userOverride: null));
    }

    [Fact]
    public void UserDeny_TakesPrecedenceOverRoleGrant()
    {
        Assert.False(PermissionAuthorizationResolver.IsAllowed(true, PermissionEffect.Deny));
        Assert.True(PermissionAuthorizationResolver.IsAllowed(false, PermissionEffect.Allow));
    }
}

public sealed class UserPermissionOverrideTests
{
    [Fact]
    public void ChangeEffect_AllowToDeny()
    {
        var userId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddMinutes(5);

        var overrideEntity = UserPermissionOverride.Create(
            userId,
            permissionId,
            PermissionEffect.Allow,
            createdAtUtc: createdAt);

        overrideEntity.ChangeEffect(PermissionEffect.Deny, updatedAt);

        Assert.Equal(PermissionEffect.Deny, overrideEntity.Effect);
        Assert.Equal(updatedAt, overrideEntity.UpdatedAtUtc);
    }

    [Fact]
    public void ChangeEffect_DenyToAllow()
    {
        var overrideEntity = UserPermissionOverride.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PermissionEffect.Deny);

        overrideEntity.ChangeEffect(PermissionEffect.Allow);

        Assert.Equal(PermissionEffect.Allow, overrideEntity.Effect);
    }
}
