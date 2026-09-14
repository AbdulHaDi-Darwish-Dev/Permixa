using Microsoft.AspNetCore.Identity;

namespace Permixa.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity role with hierarchical RoleLevel.
/// Smaller RoleLevel = higher authority.
/// RoleLevel must be assigned explicitly via the public constructor — CLR default 0 is rejected
/// so a new role cannot silently become the highest authority.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>
    /// EF Core materialization only. Not for application use.
    /// </summary>
    private ApplicationRole()
    {
    }

    public ApplicationRole(string roleName, int roleLevel)
        : base(roleName)
    {
        SetRoleLevel(roleLevel);
    }

    /// <summary>
    /// Hierarchy level. Smaller values outrank larger values.
    /// Must be greater than or equal to 1 so unintentional 0 cannot grant top authority.
    /// Explicit high-authority roles should use positive levels (e.g. Owner=1, Admin=20).
    /// </summary>
    public int RoleLevel { get; private set; }

    public void SetRoleLevel(int roleLevel)
    {
        if (roleLevel < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roleLevel),
                "RoleLevel must be >= 1. Level 0 is reserved against accidental highest authority.");
        }

        RoleLevel = roleLevel;
    }
}
