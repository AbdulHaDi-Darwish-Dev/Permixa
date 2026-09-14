namespace Permixa.AspNetCore.RateLimiting;

/// <summary>
/// Options for a Fixed Window rate-limit policy.
/// </summary>
public sealed class FixedWindowRateLimitPolicyOptions
{
    public int PermitLimit { get; set; } = 100;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Queued requests waiting for a permit. Default 0 (reject immediately).
    /// Prefer 0 for security-sensitive endpoints.
    /// </summary>
    public int QueueLimit { get; set; }

    public PermixaRateLimitPartitionKind Partition { get; set; } =
        PermixaRateLimitPartitionKind.RemoteIp;
}
