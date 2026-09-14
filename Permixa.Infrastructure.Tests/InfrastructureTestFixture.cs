using System.Data.Common;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Sessions;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Identity.Abstractions;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Permixa.Infrastructure.Tests;

public sealed class CountingCommandInterceptor : DbCommandInterceptor
{
    private int _count;

    public int Count => _count;

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Interlocked.Increment(ref _count);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Interlocked.Increment(ref _count);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}

/// <summary>
/// Shared SQL Server Testcontainer for Infrastructure integration tests.
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    public MsSqlContainer Container { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Container = new MsSqlBuilder().Build();
        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (Container is not null)
            await Container.DisposeAsync();
    }

    public string CreateUniqueDatabaseConnectionString()
    {
        var databaseName = $"PermixaTests_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(Container.GetConnectionString())
        {
            InitialCatalog = databaseName
        };
        return builder.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>
{
    public const string Name = "SqlServerCollection";
}

public sealed class InfrastructureTestFixture : IAsyncDisposable
{
    private readonly string _connectionString;

    public InfrastructureTestFixture(SqlServerContainerFixture sqlServer)
    {
        Interceptor = new CountingCommandInterceptor();
        _connectionString = sqlServer.CreateUniqueDatabaseConnectionString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(_connectionString);
            options.AddInterceptors(Interceptor);
            options.EnableSensitiveDataLogging();
        });

        services
            .AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredLength = 6;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        services.AddScoped<IPermissionRepository, Persistence.Repositories.PermissionRepository>();
        services.AddScoped<IRolePermissionRepository, Persistence.Repositories.RolePermissionRepository>();
        services.AddScoped<IUserPermissionOverrideRepository, Persistence.Repositories.UserPermissionOverrideRepository>();
        services.AddScoped<IAuthorizationStateRepository, Persistence.Repositories.AuthorizationStateRepository>();
        services.AddScoped<IIdentityUserReader, IdentityUserReader>();
        services.AddScoped<IRefreshTokenRepository, Persistence.Repositories.RefreshTokenRepository>();
        services.AddScoped<ISessionReader, Persistence.Repositories.SessionReader>();
        services.AddScoped<IIdentityRoleReader, IdentityRoleReader>();
        services.AddScoped<IUserAuthorizationVersionStore, UserAuthorizationVersionStore>();
        services.AddScoped<IRoleHierarchyReader, RoleHierarchyReader>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuthorizationStateBootstrapper, AuthorizationStateBootstrapper>();
        services.AddSingleton<IClock, Time.SystemClock>();

        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.Migrate();
    }

    public string ConnectionString => _connectionString;

    public ServiceProvider Services { get; }

    public CountingCommandInterceptor Interceptor { get; }

    public IServiceScope CreateScope() => Services.CreateScope();

    public async ValueTask DisposeAsync() => await Services.DisposeAsync();
}

[Collection(SqlServerCollection.Name)]
public abstract class InfrastructureTestBase : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServer;

    protected InfrastructureTestBase(SqlServerContainerFixture sqlServer)
    {
        _sqlServer = sqlServer;
    }

    protected InfrastructureTestFixture Fixture { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Fixture = new InfrastructureTestFixture(_sqlServer);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await Fixture.DisposeAsync();

    protected async Task<(ApplicationUser User, ApplicationRole Role)> SeedUserWithRoleAsync(
        IServiceProvider sp,
        string userName,
        string roleName,
        int roleLevel)
    {
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var db = sp.GetRequiredService<ApplicationDbContext>();

        var role = new ApplicationRole(roleName, roleLevel);
        Assert.Equal(IdentityResult.Success, await roleManager.CreateAsync(role));

        var user = new ApplicationUser(userName) { Email = $"{userName}@test.local" };
        Assert.Equal(IdentityResult.Success, await userManager.CreateAsync(user, "Passw0rd!"));
        Assert.Equal(IdentityResult.Success, await userManager.AddToRoleAsync(user, roleName));

        await db.SaveChangesAsync();
        return (user, role);
    }
}
