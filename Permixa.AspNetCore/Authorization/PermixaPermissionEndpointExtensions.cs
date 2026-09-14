using Microsoft.AspNetCore.Builder;

namespace Permixa.AspNetCore.Authorization;

/// <summary>
/// Minimal API / endpoint convention helpers for Permixa permission policies.
/// </summary>
public static class PermixaPermissionEndpointExtensions
{
    /// <summary>
    /// Requires the named Permixa permission on the endpoint.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permissionName)
        where TBuilder : IEndpointConventionBuilder
    {
        var policyName = PermixaPermissionPolicies.CreatePolicyName(permissionName);
        return builder.RequireAuthorization(policyName);
    }
}
