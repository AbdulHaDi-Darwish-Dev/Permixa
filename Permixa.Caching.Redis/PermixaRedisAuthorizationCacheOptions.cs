namespace Permixa.Caching.Redis;

/// <summary>
/// Options for the optional Redis authorization snapshot cache.
/// </summary>
public sealed class PermixaRedisAuthorizationCacheOptions
{
    /// <summary>
    /// Redis connection string. Required when Redis authorization cache is registered.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Key prefix. Final key: {KeyPrefix}user:{UserId}
    /// </summary>
    public string KeyPrefix { get; set; } = "permixa:authz:";

    /// <summary>
    /// Absolute TTL for cached snapshots. Cleanup policy only — version validation remains authoritative.
    /// </summary>
    public TimeSpan AuthorizationSnapshotTtl { get; set; } = TimeSpan.FromMinutes(30);
}
