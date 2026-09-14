using AppPermissionName = Permixa.Application.Authorization.Permissions.PermissionName;

namespace Permixa.AspNetCore.Authorization;

public static class PermixaPermissionPolicies
{
    public const string Prefix = "Permixa.Permission:";

    public static bool TryGetPermissionName(string policyName, out string permissionName)
    {
        permissionName = string.Empty;
        if (string.IsNullOrWhiteSpace(policyName)
            || !policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        permissionName = policyName[Prefix.Length..];
        return true;
    }

    public static string CreatePolicyName(string permissionName)
    {
        _ = new PermissionRequirement(permissionName);
        return Prefix + AppPermissionName.Normalize(permissionName);
    }
}
