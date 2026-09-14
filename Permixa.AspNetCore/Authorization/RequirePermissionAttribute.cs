using Microsoft.AspNetCore.Authorization;

namespace Permixa.AspNetCore.Authorization;

/// <summary>
/// Requires the caller to hold the named Permixa permission (dynamic policy).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string permissionName)
    {
        Policy = PermixaPermissionPolicies.CreatePolicyName(permissionName);
        Permission = PermissionNameFromPolicy(Policy);
    }

    public string Permission { get; }

    private static string PermissionNameFromPolicy(string policy) =>
        policy[PermixaPermissionPolicies.Prefix.Length..];
}
