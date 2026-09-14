using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Models;

namespace Permixa.Application.Tests.Authorization;

public sealed class RoleHierarchyRulesTests
{
    [Fact]
    public void SmallerLevel_MeansHigherAuthority_CanManageLowerAuthority()
    {
        Assert.True(RoleHierarchyRules.CanManage(actorLevel: 20, targetLevel: 30));
        Assert.True(RoleHierarchyRules.CanControlRoleLevel(actorLevel: 10, targetRoleLevel: 40));
    }

    [Fact]
    public void EqualLevel_IsNotManageableByDefault()
    {
        Assert.False(RoleHierarchyRules.CanManage(actorLevel: 20, targetLevel: 20));
        Assert.False(RoleHierarchyRules.CanControlRoleLevel(actorLevel: 20, targetRoleLevel: 20));
    }

    [Fact]
    public void LowerAuthorityActor_CannotManageHigherAuthorityTarget()
    {
        Assert.False(RoleHierarchyRules.CanManage(actorLevel: 30, targetLevel: 20));
        Assert.False(RoleHierarchyRules.CanControlRoleLevel(actorLevel: 40, targetRoleLevel: 10));
    }

    [Fact]
    public void EffectiveUserLevel_IsMinimumAssignedRoleLevel()
    {
        var effective = RoleHierarchyRules.ComputeEffectiveLevel([30, 50, 40]);

        Assert.Equal(30, effective);
    }

    [Fact]
    public void NoRole_DoesNotGrantAuthority()
    {
        var effective = RoleHierarchyRules.ComputeEffectiveLevel([]);

        Assert.Null(effective);
        Assert.False(RoleHierarchyRules.HasAuthority(effective));
    }
}

public sealed class AuthorizationSnapshotTests
{
    [Fact]
    public void HasPermission_UsesFinalPermissionSet()
    {
        var snapshot = new AuthorizationSnapshot(
            UserVersion: 2,
            RbacVersion: 5,
            EffectiveRoleLevel: 20,
            Permissions: new HashSet<string>(StringComparer.Ordinal) { "Users.Read", "Users.Update" });

        Assert.True(snapshot.HasPermission("Users.Read"));
        Assert.False(snapshot.HasPermission("Users.Delete"));
        Assert.Equal(20, snapshot.EffectiveRoleLevel);
    }

    [Fact]
    public void Snapshot_AllowsNullEffectiveRoleLevel()
    {
        var snapshot = new AuthorizationSnapshot(
            UserVersion: 1,
            RbacVersion: 1,
            EffectiveRoleLevel: null,
            Permissions: new HashSet<string>());

        Assert.Null(snapshot.EffectiveRoleLevel);
        Assert.Empty(snapshot.Permissions);
    }
}
