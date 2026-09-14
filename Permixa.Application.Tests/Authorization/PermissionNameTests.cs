using Permixa.Application.Authorization.Permissions;

namespace Permixa.Application.Tests.Authorization;

public sealed class PermissionNameTests
{
    [Theory]
    [InlineData("Users.Read")]
    [InlineData("Orders.Approve")]
    [InlineData("Iam.RolePermissions.Manage")]
    public void Validate_AcceptsResourceActionNames(string name)
    {
        Assert.True(PermissionName.Validate(name).IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Users")]
    [InlineData(".Read")]
    [InlineData("Users.")]
    [InlineData("Users.Read Extra")]
    [InlineData("users.read!")]
    public void Validate_RejectsInvalidNames(string name)
    {
        Assert.True(PermissionName.Validate(name).IsFailure);
    }
}
