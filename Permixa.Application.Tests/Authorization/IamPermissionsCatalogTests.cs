using Permixa.Application.Authorization;

namespace Permixa.Application.Tests.Authorization;

public sealed class IamPermissionsCatalogTests
{
    [Fact]
    public void All_ContainsApprovedIamPermissions_AndNoAdminOrPermissionDelete()
    {
        Assert.Contains(IamPermissions.Permissions.Create, IamPermissions.All);
        Assert.Contains(IamPermissions.Permissions.Read, IamPermissions.All);
        Assert.Contains(IamPermissions.Permissions.Update, IamPermissions.All);
        Assert.Contains(IamPermissions.RolePermissions.Manage, IamPermissions.All);
        Assert.Contains(IamPermissions.UserPermissionOverrides.Manage, IamPermissions.All);
        Assert.Contains(IamPermissions.Roles.Read, IamPermissions.All);
        Assert.Contains(IamPermissions.Roles.Create, IamPermissions.All);
        Assert.Contains(IamPermissions.Roles.Update, IamPermissions.All);
        Assert.Contains(IamPermissions.Roles.Delete, IamPermissions.All);
        Assert.Contains(IamPermissions.UserRoles.Manage, IamPermissions.All);
        Assert.Contains(IamPermissions.Users.Read, IamPermissions.All);
        Assert.Contains(IamPermissions.Users.Create, IamPermissions.All);
        Assert.Contains(IamPermissions.Users.Lock, IamPermissions.All);
        Assert.Contains(IamPermissions.Users.Disable, IamPermissions.All);
        Assert.Contains(IamPermissions.Users.ChangeEmail, IamPermissions.All);
        Assert.Contains(IamPermissions.Users.ForcePasswordReset, IamPermissions.All);
        Assert.Contains(IamPermissions.Sessions.Read, IamPermissions.All);
        Assert.Contains(IamPermissions.Sessions.Revoke, IamPermissions.All);
        Assert.Contains(IamPermissions.Audit.Read, IamPermissions.All);
        Assert.DoesNotContain("Iam.Admin", IamPermissions.All);
        Assert.DoesNotContain("Iam.Credentials.Admin", IamPermissions.All);
        Assert.DoesNotContain("Iam.Permissions.Delete", IamPermissions.All);
        Assert.DoesNotContain("Iam.Users.Delete", IamPermissions.All);
        Assert.Equal(19, IamPermissions.All.Count);
        Assert.Equal(IamPermissions.All.Count, IamPermissions.All.Distinct(StringComparer.Ordinal).Count());
    }
}
