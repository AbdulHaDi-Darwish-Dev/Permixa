namespace Permixa.Domain.Authorization;

/// <summary>
/// Pure domain resolution of effective permission access.
/// Precedence: User Deny &gt; User Allow &gt; Role grant &gt; Default Deny.
/// </summary>
public static class PermissionAuthorizationResolver
{
    /// <summary>
    /// Resolves whether access is allowed for a permission.
    /// </summary>
    /// <param name="roleGrantsPermission">True when any of the user's roles grants the permission.</param>
    /// <param name="userOverride">Explicit user override, or null for Inherit.</param>
    public static bool IsAllowed(bool roleGrantsPermission, PermissionEffect? userOverride)
    {
        if (userOverride == PermissionEffect.Deny)
            return false;

        if (userOverride == PermissionEffect.Allow)
            return true;

        return roleGrantsPermission;
    }
}
