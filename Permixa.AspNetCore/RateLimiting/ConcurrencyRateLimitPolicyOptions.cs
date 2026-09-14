namespace Permixa.AspNetCore.RateLimiting;

/// <summary>
/// Options for a Concurrency rate-limit policy (simultaneous in-flight requests).
/// </summary>
public sealed class ConcurrencyRateLimitPolicyOptions
{
    public int PermitLimit { get; set; } = 1;

    /// <summary>
    /// Queued requests waiting for a permit. Default 0 (reject immediately).
    /// A positive value may be useful for expensive work that can wait briefly.
    /// </summary>
    public int QueueLimit { get; set; }

    public PermixaRateLimitPartitionKind Partition { get; set; } =
        PermixaRateLimitPartitionKind.RemoteIp;
}
