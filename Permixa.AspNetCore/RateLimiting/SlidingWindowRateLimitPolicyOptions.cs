namespace Permixa.AspNetCore.RateLimiting;

/// <summary>
/// Options for a Sliding Window rate-limit policy.
/// </summary>
public sealed class SlidingWindowRateLimitPolicyOptions
{
    public int PermitLimit { get; set; } = 100;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Number of segments the window is divided into. Must be greater than 0.
    /// </summary>
    public int SegmentsPerWindow { get; set; } = 4;

    /// <summary>
    /// Queued requests waiting for a permit. Default 0 (reject immediately).
    /// Prefer 0 for security-sensitive endpoints.
    /// </summary>
    public int QueueLimit { get; set; }

    public PermixaRateLimitPartitionKind Partition { get; set; } =
        PermixaRateLimitPartitionKind.RemoteIp;
}
