namespace Permixa.Domain.Authorization;

/// <summary>
/// Explicit user permission override effect.
/// Absence of an override means Inherit (from role permissions).
/// </summary>
public enum PermissionEffect
{
    Allow = 1,
    Deny = 2
}
