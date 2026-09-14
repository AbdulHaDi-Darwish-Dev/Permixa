namespace Permixa.AspNetCore.RateLimiting;

/// <summary>
/// Built-in partition strategies for Permixa rate-limit policies.
/// Hosts needing custom keys should use ASP.NET Core <c>AddRateLimiter</c> directly.
/// </summary>
public enum PermixaRateLimitPartitionKind
{
    /// <summary>
    /// Single shared partition for the policy (all callers share one bucket).
    /// </summary>
    Global = 0,

    /// <summary>
    /// Partition by <see cref="Microsoft.AspNetCore.Http.ConnectionInfo.RemoteIpAddress"/>
    /// as resolved by ASP.NET Core (configure trusted forwarded headers on the host).
    /// </summary>
    RemoteIp = 1,

    /// <summary>
    /// Partition by authenticated user id (<c>sub</c> / NameIdentifier).
    /// Falls back to <see cref="RemoteIp"/> when the request is anonymous or the id is missing.
    /// Place <c>UseRateLimiter</c> after <c>UseAuthentication</c> when using this kind.
    /// </summary>
    AuthenticatedUserId = 2
}
