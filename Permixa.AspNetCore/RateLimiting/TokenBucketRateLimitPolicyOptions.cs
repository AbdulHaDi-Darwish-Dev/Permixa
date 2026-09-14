namespace Permixa.AspNetCore.RateLimiting;

/// <summary>
/// Options for a Token Bucket rate-limit policy.
/// </summary>
public sealed class TokenBucketRateLimitPolicyOptions
{
    public int TokenLimit { get; set; } = 10;

    public int TokensPerPeriod { get; set; } = 1;

    public TimeSpan ReplenishmentPeriod { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Queued requests waiting for a token. Default 0 (reject immediately).
    /// Prefer 0 for security-sensitive endpoints.
    /// </summary>
    public int QueueLimit { get; set; }

    /// <summary>
    /// When true, tokens replenish automatically on a timer (ASP.NET Core default behavior).
    /// </summary>
    public bool AutoReplenishment { get; set; } = true;

    public PermixaRateLimitPartitionKind Partition { get; set; } =
        PermixaRateLimitPartitionKind.RemoteIp;
}
