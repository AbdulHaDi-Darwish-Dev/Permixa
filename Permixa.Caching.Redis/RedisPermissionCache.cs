using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace Permixa.Caching.Redis;

/// <summary>
/// Redis-backed <see cref="IPermissionCache"/>. Redis is disposable cache only; SQL Server remains authoritative.
/// </summary>
public sealed class RedisPermissionCache : IPermissionCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly PermixaRedisAuthorizationCacheOptions _options;
    private readonly ILogger<RedisPermissionCache> _logger;

    public RedisPermissionCache(
        IConnectionMultiplexer redis,
        IOptions<PermixaRedisAuthorizationCacheOptions> options,
        ILogger<RedisPermissionCache> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AuthorizationSnapshot?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = BuildKey(userId);
            var value = await db.StringGetAsync(key);

            if (value.IsNullOrEmpty)
                return null;

            try
            {
                var dto = JsonSerializer.Deserialize<RedisAuthorizationSnapshotDto>((string)value!, JsonOptions);
                if (dto is null)
                {
                    await TryDeleteCorruptAsync(db, key);
                    return null;
                }

                return dto.ToSnapshot();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Corrupt authorization snapshot cache entry for user {UserId}; treating as miss.", userId);
                await TryDeleteCorruptAsync(db, key);
                return null;
            }
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis GET failed for authorization snapshot user {UserId}; treating as miss.", userId);
            return null;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis GET timed out for authorization snapshot user {UserId}; treating as miss.", userId);
            return null;
        }
    }

    public async Task SetAsync(
        Guid userId,
        AuthorizationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = BuildKey(userId);
            var dto = RedisAuthorizationSnapshotDto.FromSnapshot(snapshot);
            var json = JsonSerializer.Serialize(dto, JsonOptions);
            await db.StringSetAsync(key, json, _options.AuthorizationSnapshotTtl);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis SET failed for authorization snapshot user {UserId}; continuing without cache write.", userId);
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis SET timed out for authorization snapshot user {UserId}; continuing without cache write.", userId);
        }
    }

    public async Task RemoveAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(BuildKey(userId));
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis REMOVE failed for authorization snapshot user {UserId}.", userId);
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis REMOVE timed out for authorization snapshot user {UserId}.", userId);
        }
    }

    internal string BuildKey(Guid userId) =>
        $"{_options.KeyPrefix}user:{userId:D}";

    private async Task TryDeleteCorruptAsync(IDatabase db, RedisKey key)
    {
        try
        {
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            _logger.LogWarning(ex, "Best-effort delete of corrupt authorization cache key failed.");
        }
    }

    private sealed class RedisAuthorizationSnapshotDto
    {
        public int UserVersion { get; set; }

        public int RbacVersion { get; set; }

        public int? EffectiveRoleLevel { get; set; }

        public string[] Permissions { get; set; } = Array.Empty<string>();

        public static RedisAuthorizationSnapshotDto FromSnapshot(AuthorizationSnapshot snapshot) =>
            new()
            {
                UserVersion = snapshot.UserVersion,
                RbacVersion = snapshot.RbacVersion,
                EffectiveRoleLevel = snapshot.EffectiveRoleLevel,
                Permissions = snapshot.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToArray()
            };

        public AuthorizationSnapshot ToSnapshot() =>
            new(
                UserVersion,
                RbacVersion,
                EffectiveRoleLevel,
                Permissions.ToHashSet(StringComparer.Ordinal));
    }
}
