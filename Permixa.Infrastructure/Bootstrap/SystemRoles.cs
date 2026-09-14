using Permixa.Application.Authorization;

namespace Permixa.Infrastructure.Bootstrap;

/// <summary>
/// Centralized Permixa system role names and levels.
/// </summary>
public static class SystemRoles
{
    public const string Owner = PermixaRoles.Owner;

    /// <summary>
    /// Highest standard authority. Smaller RoleLevel = higher authority. Must remain &gt;= 1.
    /// </summary>
    public const int OwnerRoleLevel = 1;
}
