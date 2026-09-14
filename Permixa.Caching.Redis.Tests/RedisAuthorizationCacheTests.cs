using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Models;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authorization;
using Permixa.Caching.Redis;
using Permixa.Infrastructure;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Permixa.Infrastructure.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Permixa.Caching.Redis.Tests;

[CollectionDefinition(Name)]
public sealed class RedisCollection : ICollectionFixture<RedisContainerFixture>
{
    public const string Name = "RedisCollection";
}

public sealed class RedisContainerFixture : IAsyncLifetime
{
    public RedisContainer Container { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Container = new RedisBuilder().Build();
        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (Container is not null)
            await Container.DisposeAsync();
    }

    public string ConnectionString => Container.GetConnectionString();
}

[CollectionDefinition(Name)]
public sealed class SqlAndRedisCollection :
    ICollectionFixture<SqlServerContainerFixture>,
    ICollectionFixture<RedisContainerFixture>
{
    public const string Name = "SqlAndRedisCollection";
}

[Collection(RedisCollection.Name)]
public sealed class RedisPermissionCacheTests : IAsyncLifetime
{
    private readonly RedisContainerFixture _redis;
    private ServiceProvider _sp = null!;
    private IPermissionCache _cache = null!;
    private IConnectionMultiplexer _multiplexer = null!;

    public RedisPermissionCacheTests(RedisContainerFixture redis)
    {
        _redis = redis;
    }

    public Task InitializeAsync()
    {
        _sp = BuildCacheProvider(TimeSpan.FromMinutes(30));
        _cache = _sp.GetRequiredService<IPermissionCache>();
        _multiplexer = _sp.GetRequiredService<IConnectionMultiplexer>();
        Assert.IsType<RedisPermissionCache>(_cache);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _sp.DisposeAsync();

    [Fact]
    public async Task Set_ThenGet_RoundTripsSnapshot()
    {
        var userId = Guid.NewGuid();
        var snapshot = new AuthorizationSnapshot(
            3,
            11,
            20,
            new HashSet<string>(StringComparer.Ordinal) { "Orders.Write", "Orders.Read" });

        await _cache.SetAsync(userId, snapshot);
        var loaded = await _cache.GetAsync(userId);

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.UserVersion);
        Assert.Equal(11, loaded.RbacVersion);
        Assert.Equal(20, loaded.EffectiveRoleLevel);
        Assert.Equal(2, loaded.Permissions.Count);
        Assert.Contains("Orders.Read", loaded.Permissions);
        Assert.Contains("Orders.Write", loaded.Permissions);
    }

    [Fact]
    public async Task Get_UnknownKey_ReturnsNull()
    {
        var loaded = await _cache.GetAsync(Guid.NewGuid());
        Assert.Null(loaded);
    }

    [Fact]
    public async Task Set_Overwrite_ReturnsLatest()
    {
        var userId = Guid.NewGuid();
        await _cache.SetAsync(userId, new AuthorizationSnapshot(1, 1, 10, new HashSet<string> { "A" }));
        await _cache.SetAsync(userId, new AuthorizationSnapshot(2, 5, 30, new HashSet<string> { "B", "C" }));

        var loaded = await _cache.GetAsync(userId);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.UserVersion);
        Assert.Equal(5, loaded.RbacVersion);
        Assert.Equal(30, loaded.EffectiveRoleLevel);
        Assert.Contains("B", loaded.Permissions);
        Assert.Contains("C", loaded.Permissions);
        Assert.DoesNotContain("A", loaded.Permissions);
    }

    [Fact]
    public async Task Remove_DeletesEntry()
    {
        var userId = Guid.NewGuid();
        await _cache.SetAsync(userId, new AuthorizationSnapshot(1, 1, null, new HashSet<string> { "X" }));
        await _cache.RemoveAsync(userId);
        Assert.Null(await _cache.GetAsync(userId));
    }

    [Fact]
    public async Task Entry_Expires_AfterConfiguredTtl()
    {
        await using var shortTtlSp = BuildCacheProvider(TimeSpan.FromSeconds(2));
        var cache = shortTtlSp.GetRequiredService<IPermissionCache>();
        var userId = Guid.NewGuid();

        await cache.SetAsync(userId, new AuthorizationSnapshot(1, 1, 1, new HashSet<string> { "T" }));
        Assert.NotNull(await cache.GetAsync(userId));

        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Null(await cache.GetAsync(userId));
    }

    [Fact]
    public async Task CorruptValue_IsTreatedAsMiss_AndKeyDeleted()
    {
        var userId = Guid.NewGuid();
        var key = $"permixa:authz:test:user:{userId:D}";
        await _multiplexer.GetDatabase().StringSetAsync(key, "{not-json");

        Assert.Null(await _cache.GetAsync(userId));
        Assert.False(await _multiplexer.GetDatabase().KeyExistsAsync(key));
    }

    [Fact]
    public async Task RedisOutage_GetReturnsMiss_WithoutThrowing()
    {
        await using var badSp = BuildCacheProvider(
            TimeSpan.FromMinutes(30),
            connectionString: "localhost:1");
        var cache = badSp.GetRequiredService<IPermissionCache>();

        var result = await cache.GetAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    private ServiceProvider BuildCacheProvider(TimeSpan ttl, string? connectionString = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaRedisAuthorizationCache(o =>
        {
            o.ConnectionString = connectionString ?? _redis.ConnectionString;
            o.KeyPrefix = "permixa:authz:test:";
            o.AuthorizationSnapshotTtl = ttl;
        });
        return services.BuildServiceProvider();
    }
}

[Collection(SqlAndRedisCollection.Name)]
public sealed class RedisAuthorizationEndToEndTests : IAsyncLifetime
{
    private readonly RedisContainerFixture _redis;
    private readonly SqlServerContainerFixture _sql;
    private string _sqlConnection = null!;
    private ServiceProvider _sp = null!;
    private CountingCommandInterceptor _interceptor = null!;

    public RedisAuthorizationEndToEndTests(
        RedisContainerFixture redis,
        SqlServerContainerFixture sql)
    {
        _redis = redis;
        _sql = sql;
    }

    public async Task InitializeAsync()
    {
        _sqlConnection = _sql.CreateUniqueDatabaseConnectionString();
        _interceptor = new CountingCommandInterceptor();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString = _sqlConnection;
        });

        // Replace DbContext registration is awkward; add interceptor via post-config.
        // Rebuild with custom DbContext that includes interceptor.
        services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(db =>
        {
            db.UseSqlServer(_sqlConnection);
            db.AddInterceptors(_interceptor);
            db.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
        });
        services
            .AddIdentityCore<ApplicationUser>(identity =>
            {
                identity.User.RequireUniqueEmail = true;
                identity.Password.RequiredLength = 6;
                identity.Password.RequireDigit = false;
                identity.Password.RequireLowercase = false;
                identity.Password.RequireUppercase = false;
                identity.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        services.AddScoped<IPermissionRepository, Permixa.Infrastructure.Persistence.Repositories.PermissionRepository>();
        services.AddScoped<IRolePermissionRepository, Permixa.Infrastructure.Persistence.Repositories.RolePermissionRepository>();
        services.AddScoped<IUserPermissionOverrideRepository, Permixa.Infrastructure.Persistence.Repositories.UserPermissionOverrideRepository>();
        services.AddScoped<IAuthorizationStateRepository, Permixa.Infrastructure.Persistence.Repositories.AuthorizationStateRepository>();
        services.AddScoped<IIdentityUserReader, IdentityUserReader>();
        services.AddScoped<IIdentityRoleReader, IdentityRoleReader>();
        services.AddScoped<IUserAuthorizationVersionStore, UserAuthorizationVersionStore>();
        services.AddScoped<IRoleHierarchyReader, RoleHierarchyReader>();
        services.AddScoped<IAuthorizationHierarchyService, Application.Authorization.Hierarchy.AuthorizationHierarchyService>();
        services.AddScoped<IEffectivePermissionService, Application.Authorization.EffectivePermissions.EffectivePermissionService>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IClock, Permixa.Infrastructure.Time.SystemClock>();
        services.AddScoped<IAuthorizationStateBootstrapper, AuthorizationStateBootstrapper>();

        services.AddPermixaRedisAuthorizationCache(o =>
        {
            o.ConnectionString = _redis.ConnectionString;
            o.KeyPrefix = "permixa:authz:e2e:";
            o.AuthorizationSnapshotTtl = TimeSpan.FromMinutes(30);
        });

        _sp = services.BuildServiceProvider();

        using var scope = _sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IAuthorizationStateBootstrapper>().EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _sp.DisposeAsync();

    [Fact]
    public async Task SecondResolution_UsesCache_SkipsPermissionReconstruction()
    {
        var (userId, _) = await SeedAuthorizedUserAsync("cache.user", "Cache.Read");

        using (var scope = _sp.CreateScope())
        {
            var effective = scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>();
            _interceptor.Reset();
            var first = await effective.GetAuthorizationSnapshotAsync(userId);
            Assert.True(first.IsSuccess);
            Assert.Contains("Cache.Read", first.Value.Permissions);
            var missQueries = _interceptor.Count;
            Assert.True(missQueries >= 4, $"Expected rebuild query path, got {missQueries}");

            _interceptor.Reset();
            var second = await effective.GetAuthorizationSnapshotAsync(userId);
            Assert.True(second.IsSuccess);
            Assert.Contains("Cache.Read", second.Value.Permissions);
            var hitQueries = _interceptor.Count;

            // Hit still reads user existence + versions, but not RolePermissions/overrides reconstruction.
            Assert.True(hitQueries < missQueries, $"Expected fewer queries on cache hit ({hitQueries} vs {missQueries})");
            Assert.True(hitQueries <= 4, $"Expected bounded version/existence reads, got {hitQueries}");
        }

        var redis = _sp.GetRequiredService<IConnectionMultiplexer>();
        Assert.True(await redis.GetDatabase().KeyExistsAsync($"permixa:authz:e2e:user:{userId:D}"));
    }

    [Fact]
    public async Task UserVersionChange_InvalidatesCachedSnapshot()
    {
        var (userId, permissionId) = await SeedAuthorizedUserAsync("user.version", "User.Read");

        using (var scope = _sp.CreateScope())
        {
            var effective = scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>();
            var first = await effective.GetAuthorizationSnapshotAsync(userId);
            Assert.True(first.IsSuccess);
            Assert.Equal(1, first.Value.UserVersion);
        }

        using (var scope = _sp.CreateScope())
        {
            var overrides = scope.ServiceProvider.GetRequiredService<IUserPermissionOverrideRepository>();
            var versions = scope.ServiceProvider.GetRequiredService<IUserAuthorizationVersionStore>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await overrides.AddAsync(
                UserPermissionOverride.Create(userId, permissionId, PermissionEffect.Deny));
            await versions.IncrementAsync(userId);
            await uow.SaveChangesAsync();
        }

        using (var scope = _sp.CreateScope())
        {
            var effective = scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>();
            var second = await effective.GetAuthorizationSnapshotAsync(userId);
            Assert.True(second.IsSuccess);
            Assert.Equal(2, second.Value.UserVersion);
            Assert.DoesNotContain("User.Read", second.Value.Permissions);
        }
    }

    [Fact]
    public async Task RbacVersionChange_InvalidatesCachedSnapshot_WithoutMassDelete()
    {
        var (userId, permissionId) = await SeedAuthorizedUserAsync("rbac.version", "Rbac.Read");

        using (var scope = _sp.CreateScope())
        {
            var effective = scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>();
            var first = await effective.GetAuthorizationSnapshotAsync(userId);
            Assert.True(first.IsSuccess);
            Assert.Equal(1, first.Value.RbacVersion);
            Assert.Contains("Rbac.Read", first.Value.Permissions);
        }

        var redis = _sp.GetRequiredService<IConnectionMultiplexer>();
        var key = $"permixa:authz:e2e:user:{userId:D}";
        Assert.True(await redis.GetDatabase().KeyExistsAsync(key));

        using (var scope = _sp.CreateScope())
        {
            var rolePermissions = scope.ServiceProvider.GetRequiredService<IRolePermissionRepository>();
            var states = scope.ServiceProvider.GetRequiredService<IAuthorizationStateRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var role = await roleManager.FindByNameAsync("rbac.version-role");
            Assert.NotNull(role);
            await rolePermissions.RemoveAsync(role!.Id, permissionId);
            var state = await states.GetAsync();
            Assert.NotNull(state);
            state!.IncrementRbacVersion();
            await uow.SaveChangesAsync();
        }

        // Physical key may still exist with stale payload — logical invalidation must reject it.
        Assert.True(await redis.GetDatabase().KeyExistsAsync(key));

        using (var scope = _sp.CreateScope())
        {
            var effective = scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>();
            var second = await effective.GetAuthorizationSnapshotAsync(userId);
            Assert.True(second.IsSuccess);
            Assert.Equal(2, second.Value.RbacVersion);
            Assert.DoesNotContain("Rbac.Read", second.Value.Permissions);
        }
    }

    [Fact]
    public async Task RedisOutage_FallsBackToSqlAuthorization()
    {
        var (userId, _) = await SeedAuthorizedUserAsync("outage.user", "Outage.Read");

        await using var outageSp = BuildSqlOnlyWithBrokenRedis();
        using var scope = outageSp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        // Same DB already migrated; EnsureCreated state already exists from shared connection string DB.

        var effective = scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>();
        var result = await effective.GetAuthorizationSnapshotAsync(userId);
        Assert.True(result.IsSuccess);
        Assert.Contains("Outage.Read", result.Value.Permissions);
    }

    private ServiceProvider BuildSqlOnlyWithBrokenRedis()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(db =>
        {
            db.UseSqlServer(_sqlConnection);
            db.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
        });
        services
            .AddIdentityCore<ApplicationUser>(identity =>
            {
                identity.User.RequireUniqueEmail = true;
                identity.Password.RequiredLength = 6;
                identity.Password.RequireDigit = false;
                identity.Password.RequireLowercase = false;
                identity.Password.RequireUppercase = false;
                identity.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        services.AddScoped<IPermissionRepository, Permixa.Infrastructure.Persistence.Repositories.PermissionRepository>();
        services.AddScoped<IRolePermissionRepository, Permixa.Infrastructure.Persistence.Repositories.RolePermissionRepository>();
        services.AddScoped<IUserPermissionOverrideRepository, Permixa.Infrastructure.Persistence.Repositories.UserPermissionOverrideRepository>();
        services.AddScoped<IAuthorizationStateRepository, Permixa.Infrastructure.Persistence.Repositories.AuthorizationStateRepository>();
        services.AddScoped<IIdentityUserReader, IdentityUserReader>();
        services.AddScoped<IIdentityRoleReader, IdentityRoleReader>();
        services.AddScoped<IUserAuthorizationVersionStore, UserAuthorizationVersionStore>();
        services.AddScoped<IRoleHierarchyReader, RoleHierarchyReader>();
        services.AddScoped<IEffectivePermissionService, Application.Authorization.EffectivePermissions.EffectivePermissionService>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IClock, Permixa.Infrastructure.Time.SystemClock>();

        services.AddPermixaRedisAuthorizationCache(o =>
        {
            o.ConnectionString = "localhost:1";
            o.KeyPrefix = "permixa:authz:outage:";
        });

        return services.BuildServiceProvider();
    }

    private async Task<(Guid UserId, Guid PermissionId)> SeedAuthorizedUserAsync(
        string userName,
        string permissionName)
    {
        using var scope = _sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var permissions = scope.ServiceProvider.GetRequiredService<IPermissionRepository>();
        var rolePermissions = scope.ServiceProvider.GetRequiredService<IRolePermissionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var role = new ApplicationRole($"{userName}-role", 20);
        Assert.Equal(IdentityResult.Success, await roleManager.CreateAsync(role));

        var user = new ApplicationUser(userName) { Email = $"{userName}@test.local" };
        Assert.Equal(IdentityResult.Success, await userManager.CreateAsync(user, "Passw0rd!"));
        Assert.Equal(IdentityResult.Success, await userManager.AddToRoleAsync(user, role.Name!));

        var permission = Permission.Create(permissionName);
        await permissions.AddAsync(permission);
        await rolePermissions.AddAsync(RolePermission.Create(role.Id, permission.Id));
        await uow.SaveChangesAsync();

        return (user.Id, permission.Id);
    }
}
