using Permixa.Application.Authorization.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Permixa.Caching.Redis;

public static class DependencyInjection
{
    /// <summary>
    /// Registers Redis as the <see cref="IPermissionCache"/> implementation for authorization snapshots.
    /// Call after core Permixa infrastructure registration. Replaces the default memory cache registration
    /// by registering a later <see cref="IPermissionCache"/> singleton (last registration wins with GetRequiredService).
    /// </summary>
    public static IServiceCollection AddPermixaRedisAuthorizationCache(
        this IServiceCollection services,
        Action<PermixaRedisAuthorizationCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new PermixaRedisAuthorizationCacheOptions();
        configure?.Invoke(options);
        ValidateRedisOptions(options);

        services.AddSingleton(Options.Create(CloneRedisOptions(options)));

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var configuration = ConfigurationOptions.Parse(options.ConnectionString!);
            configuration.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(configuration);
        });

        services.AddSingleton<IPermissionCache, RedisPermissionCache>();

        return services;
    }

    private static void ValidateRedisOptions(PermixaRedisAuthorizationCacheOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "Permixa Redis authorization cache requires a ConnectionString. " +
                "Configure PermixaRedisAuthorizationCacheOptions.ConnectionString when calling AddPermixaRedisAuthorizationCache.");
        }

        if (string.IsNullOrWhiteSpace(options.KeyPrefix))
        {
            throw new InvalidOperationException(
                "Permixa Redis authorization cache KeyPrefix cannot be blank.");
        }

        if (options.AuthorizationSnapshotTtl <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Permixa Redis authorization cache AuthorizationSnapshotTtl must be greater than zero.");
        }
    }

    private static PermixaRedisAuthorizationCacheOptions CloneRedisOptions(
        PermixaRedisAuthorizationCacheOptions source) =>
        new()
        {
            ConnectionString = source.ConnectionString,
            KeyPrefix = source.KeyPrefix,
            AuthorizationSnapshotTtl = source.AuthorizationSnapshotTtl
        };
}
