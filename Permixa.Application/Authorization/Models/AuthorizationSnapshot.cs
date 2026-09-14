namespace Permixa.Application.Authorization.Models;

/// <summary>
/// Versioned effective authorization state for a user.
/// Contains final effective permissions and hierarchy level — not raw role grants + overrides.
/// </summary>
public sealed record AuthorizationSnapshot(
    int UserVersion,
    int RbacVersion,
    int? EffectiveRoleLevel,
    IReadOnlySet<string> Permissions)
{
    public bool HasPermission(string permissionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionName);
        return Permissions.Contains(permissionName);
    }
}
