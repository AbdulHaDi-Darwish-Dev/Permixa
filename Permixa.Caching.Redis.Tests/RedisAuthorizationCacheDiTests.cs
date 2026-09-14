using Permixa.Application.Authorization.Abstractions;
using Permixa.Infrastructure;
using Permixa.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.Caching.Redis.Tests;

public sealed class RedisAuthorizationCacheDiTests
{
    [Fact]
    public void AddPermixaInfrastructure_DefaultsToMemoryPermissionCache()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString = "Server=.;Database=x;Trusted_Connection=True;TrustServerCertificate=True;";
        });

        using var sp = services.BuildServiceProvider();
        Assert.IsType<MemoryPermissionCache>(sp.GetRequiredService<IPermissionCache>());
    }

    [Fact]
    public void AddPermixaRedisAuthorizationCache_OverridesToRedisPermissionCache()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString = "Server=.;Database=x;Trusted_Connection=True;TrustServerCertificate=True;";
        });
        services.AddPermixaRedisAuthorizationCache(o =>
        {
            o.ConnectionString = "localhost:6379";
            o.KeyPrefix = "permixa:authz:";
        });

        using var sp = services.BuildServiceProvider();
        Assert.IsType<RedisPermissionCache>(sp.GetRequiredService<IPermissionCache>());
    }

    [Fact]
    public void AddPermixaRedisAuthorizationCache_AfterMemory_LastRegistrationWins()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString = "Server=.;Database=x;Trusted_Connection=True;TrustServerCertificate=True;";
        });
        services.AddPermixaRedisAuthorizationCache(o =>
        {
            o.ConnectionString = "localhost:6379";
            o.KeyPrefix = "permixa:authz:";
        });

        // Explicit re-register of memory after Redis must win (last registration semantics).
        services.AddSingleton<IPermissionCache, MemoryPermissionCache>();

        using var sp = services.BuildServiceProvider();
        Assert.IsType<MemoryPermissionCache>(sp.GetRequiredService<IPermissionCache>());
    }

    [Fact]
    public void AddPermixaRedisAuthorizationCache_WithoutConnectionString_FailsFast()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPermixaRedisAuthorizationCache(o =>
            {
                o.ConnectionString = " ";
                o.KeyPrefix = "permixa:authz:";
            }));

        Assert.Contains("ConnectionString", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddPermixaRedisAuthorizationCache_BlankPrefix_FailsFast()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPermixaRedisAuthorizationCache(o =>
            {
                o.ConnectionString = "localhost:6379";
                o.KeyPrefix = " ";
            }));

        Assert.Contains("KeyPrefix", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddPermixaRedisAuthorizationCache_NonPositiveTtl_FailsFast()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPermixaRedisAuthorizationCache(o =>
            {
                o.ConnectionString = "localhost:6379";
                o.KeyPrefix = "permixa:authz:";
                o.AuthorizationSnapshotTtl = TimeSpan.Zero;
            }));

        Assert.Contains("AuthorizationSnapshotTtl", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
