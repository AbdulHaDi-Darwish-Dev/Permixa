namespace Permixa.Domain.Authorization;

/// <summary>
/// Explicit many-to-many assignment of a permission to a role.
/// </summary>
public sealed class RolePermission
{
    private RolePermission(Guid roleId, Guid permissionId, DateTime assignedAtUtc)
    {
        RoleId = roleId;
        PermissionId = permissionId;
        AssignedAtUtc = assignedAtUtc;
    }

    public Guid RoleId { get; }

    public Guid PermissionId { get; }

    public DateTime AssignedAtUtc { get; }

    public static RolePermission Create(Guid roleId, Guid permissionId, DateTime? assignedAtUtc = null)
    {
        if (roleId == Guid.Empty)
            throw new ArgumentException("Role id cannot be empty.", nameof(roleId));

        if (permissionId == Guid.Empty)
            throw new ArgumentException("Permission id cannot be empty.", nameof(permissionId));

        return new RolePermission(roleId, permissionId, assignedAtUtc ?? DateTime.UtcNow);
    }
}
