using Permixa.Domain.Authorization;
using Permixa.Domain.Common;

namespace Permixa.Domain.Tests.Authorization;

public sealed class PermissionValidationTests
{
    [Fact]
    public void Permission_RejectsEmptyName()
    {
        Assert.Throws<DomainException>(() => Permission.Create(""));
    }

    [Fact]
    public void Permission_TrimsName()
    {
        var permission = Permission.Create(" Users.Read ");

        Assert.Equal("Users.Read", permission.Name);
    }
}
