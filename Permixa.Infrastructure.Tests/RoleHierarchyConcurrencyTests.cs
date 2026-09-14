using System.Diagnostics;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Roles.ChangePosition;
using Permixa.Application.Authorization.Roles.Create;
using Permixa.Application.Common.Abstractions;
using Permixa.Infrastructure;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.Infrastructure.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class RoleHierarchyConcurrencyTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServer;
    private string _connectionString = null!;

    public RoleHierarchyConcurrencyTests(SqlServerContainerFixture sqlServer)
    {
        _sqlServer = sqlServer;
    }

    public Task InitializeAsync()
    {
        _connectionString = _sqlServer.CreateUniqueDatabaseConnectionString();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task HierarchyLock_BlocksSecondWriterUntilFirstCommits()
    {
        await using var sp = BuildProvider();
        await SeedOwnerAndAdminAsync(sp);

        var firstAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            await using var scope = sp.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var hierarchyLock = scope.ServiceProvider.GetRequiredService<IAuthorizationHierarchyWriteLock>();
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                Assert.NotNull(await hierarchyLock.AcquireAsync(ct));
                firstAcquired.SetResult();
                await Task.Delay(1500, ct);
            });
        });

        await firstAcquired.Task;
        var wait = Stopwatch.StartNew();

        await using (var scope = sp.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var hierarchyLock = scope.ServiceProvider.GetRequiredService<IAuthorizationHierarchyWriteLock>();
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                Assert.NotNull(await hierarchyLock.AcquireAsync(ct));
            });
        }

        wait.Stop();
        await first;
        Assert.True(wait.ElapsedMilliseconds >= 1000, $"Second writer waited only {wait.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task SequentialCreateBelowSameReference_SecondWriterShiftsFirstOffTarget()
    {
        await using var sp = BuildProvider();
        var (ownerId, adminId) = await SeedOwnerAndAdminAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var create = scope.ServiceProvider.GetRequiredService<CreateRoleUseCase>();
            var first = await create.ExecuteAsync(
                new CreateRoleCommand(ownerId, "SequentialA", adminId, RolePlacement.Below));
            var second = await create.ExecuteAsync(
                new CreateRoleCommand(ownerId, "SequentialB", adminId, RolePlacement.Below));
            Assert.True(first.IsSuccess, first.Error?.Description);
            Assert.True(second.IsSuccess, second.Error?.Description);
            Assert.Equal(11, first.Value.RoleLevel);
            Assert.Equal(11, second.Value.RoleLevel);
        }

        using var verify = sp.CreateScope();
        var levels = await ReadLevelsAsync(verify.ServiceProvider, "SequentialA", "SequentialB");
        Assert.Equal(11, levels["SequentialB"]);
        Assert.Equal(12, levels["SequentialA"]);
    }

    [Fact]
    public async Task ConcurrentCreateBelowSameReference_ProducesDistinctPersistedTiers()
    {
        await using var sp = BuildProvider();
        var (ownerId, adminId) = await SeedOwnerAndAdminAsync(sp);

        await using var scope1 = sp.CreateAsyncScope();
        await using var scope2 = sp.CreateAsyncScope();
        var create1 = scope1.ServiceProvider.GetRequiredService<CreateRoleUseCase>();
        var create2 = scope2.ServiceProvider.GetRequiredService<CreateRoleUseCase>();

        var results = await Task.WhenAll(
            Task.Run(() => create1.ExecuteAsync(new CreateRoleCommand(ownerId, "ConcurrentA", adminId, RolePlacement.Below))),
            Task.Run(() => create2.ExecuteAsync(new CreateRoleCommand(ownerId, "ConcurrentB", adminId, RolePlacement.Below))));

        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error?.Description));

        using var verify = sp.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var created = await db.Roles
            .Where(r => r.Name == "ConcurrentA" || r.Name == "ConcurrentB")
            .Select(r => new { r.Name, r.RoleLevel })
            .ToListAsync();

        Assert.Equal(2, created.Count);
        Assert.Equal(2, created.Select(r => r.RoleLevel).Distinct().Count());
        Assert.Equal(new HashSet<int> { 11, 12 }, created.Select(r => r.RoleLevel).ToHashSet());
        Assert.Equal(1, (await db.Roles.SingleAsync(r => r.Name == SystemRoles.Owner)).RoleLevel);
    }

    [Fact]
    public async Task ConcurrentChangeRolePosition_ProducesDistinctPersistedTiers()
    {
        await using var sp = BuildProvider();
        var (ownerId, adminId) = await SeedOwnerAndAdminAsync(sp);
        Guid leftId, rightId;

        using (var scope = sp.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var left = new ApplicationRole("Left", 20);
            var right = new ApplicationRole("Right", 30);
            Assert.Equal(IdentityResult.Success, await roles.CreateAsync(left));
            Assert.Equal(IdentityResult.Success, await roles.CreateAsync(right));
            leftId = left.Id;
            rightId = right.Id;
        }

        await using var scope1 = sp.CreateAsyncScope();
        await using var scope2 = sp.CreateAsyncScope();
        var move1 = scope1.ServiceProvider.GetRequiredService<ChangeRolePositionUseCase>();
        var move2 = scope2.ServiceProvider.GetRequiredService<ChangeRolePositionUseCase>();

        var results = await Task.WhenAll(
            Task.Run(() => move1.ExecuteAsync(new ChangeRolePositionCommand(ownerId, leftId, adminId, RolePlacement.Below))),
            Task.Run(() => move2.ExecuteAsync(new ChangeRolePositionCommand(ownerId, rightId, adminId, RolePlacement.Below))));

        var successes = results.Where(r => r.IsSuccess).ToList();
        Assert.True(successes.Count >= 1);
        if (successes.Count < 2)
            Assert.Contains(results, r => r.IsFailure);

        using var verify = sp.CreateScope();
        var levels = await ReadLevelsAsync(verify.ServiceProvider, "Left", "Right");
        Assert.Equal(2, levels.Values.Distinct().Count());
        Assert.DoesNotContain(levels.Values, level => level <= 0);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString = _connectionString;
            o.Bootstrap.Enabled = true;
            o.Bootstrap.OwnerEmail = "owner@permixa.test";
            o.Bootstrap.OwnerUserName = "permixa.owner";
            o.Bootstrap.OwnerPassword = "OwnerPass1!";
        });
        services.AddPermixaAuthorization();
        return services.BuildServiceProvider();
    }

    private static async Task<(Guid OwnerId, Guid AdminId)> SeedOwnerAndAdminAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
        var owner = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync("owner@permixa.test");
        var admin = new ApplicationRole("Admin", 10);
        Assert.Equal(
            IdentityResult.Success,
            await scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>().CreateAsync(admin));
        return (owner!.Id, admin.Id);
    }

    private static async Task<Dictionary<string, int>> ReadLevelsAsync(IServiceProvider sp, params string[] names)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        return await db.Roles
            .Where(r => names.Contains(r.Name))
            .ToDictionaryAsync(r => r.Name!, r => r.RoleLevel);
    }
}
