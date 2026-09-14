using System.Net;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Roles.Create;
using Permixa.Application.Authorization.UserRoles.Assign;
using Permixa.Application.Authorization.UserRoles.Remove;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Persistence;
using Permixa.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Permixa.IntegrationTests;

public sealed class RoleAdministrationFlowTests : IntegrationTestBase
{
    public RoleAdministrationFlowTests(SqlServerContainerFixture sql, RedisContainerFixture redis)
        : base(sql, redis)
    {
    }

    [SkippableFact]
    public async Task Owner_BuildsHierarchy_AssignsRole_ThenStaleSnapshotIsRejected()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.BootstrapAsync();

        var owner = await host.LoginAsync(TestKeys.OwnerEmail, TestKeys.OwnerPassword);
        var ownerRoleId = await host.ExecuteScopedAsync(async sp =>
            (await sp.GetRequiredService<ApplicationDbContext>().Roles
                .SingleAsync(r => r.Name == SystemRoles.Owner)).Id);

        var admin = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<CreateRoleUseCase>().ExecuteAsync(
                new CreateRoleCommand(owner.UserId, "Admin", ownerRoleId, RolePlacement.Below)));
        Assert.True(admin.IsSuccess, admin.Error?.Description);
        Assert.Equal(2, admin.Value.RoleLevel);

        var manager = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<CreateRoleUseCase>().ExecuteAsync(
                new CreateRoleCommand(owner.UserId, "Manager", admin.Value.Id, RolePlacement.Below)));
        Assert.True(manager.IsSuccess, manager.Error?.Description);
        Assert.Equal(3, manager.Value.RoleLevel);

        var employee = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<CreateRoleUseCase>().ExecuteAsync(
                new CreateRoleCommand(owner.UserId, "Employee", manager.Value.Id, RolePlacement.Below)));
        Assert.True(employee.IsSuccess, employee.Error?.Description);
        Assert.Equal(4, employee.Value.RoleLevel);

        var supervisor = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<CreateRoleUseCase>().ExecuteAsync(
                new CreateRoleCommand(owner.UserId, "Supervisor", manager.Value.Id, RolePlacement.SameLevel)));
        Assert.True(supervisor.IsSuccess, supervisor.Error?.Description);
        Assert.Equal(3, supervisor.Value.RoleLevel);

        var teamLead = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<CreateRoleUseCase>().ExecuteAsync(
                new CreateRoleCommand(owner.UserId, "TeamLead", manager.Value.Id, RolePlacement.Below)));
        Assert.True(teamLead.IsSuccess, teamLead.Error?.Description);
        Assert.Equal(4, teamLead.Value.RoleLevel);

        await host.ExecuteScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var levels = await db.Roles.ToDictionaryAsync(r => r.Name!, r => r.RoleLevel);
            Assert.Equal(1, levels[SystemRoles.Owner]);
            Assert.Equal(2, levels["Admin"]);
            Assert.Equal(3, levels["Manager"]);
            Assert.Equal(3, levels["Supervisor"]);
            Assert.Equal(4, levels["TeamLead"]);
            Assert.Equal(5, levels["Employee"]);
        });

        var permissionId = await host.CreatePermissionAsync("Orders.Read");
        await host.AssignPermissionToRoleAsync(owner.UserId, employee.Value.Id, permissionId);

        var clerk = await host.RegisterAsync("clerk", "clerk@permixa.test", TestKeys.UserPassword);
        await host.ExecuteScopedAsync(async sp =>
        {
            var assigned = await sp.GetRequiredService<AssignRoleToUserUseCase>()
                .ExecuteAsync(new AssignRoleToUserCommand(owner.UserId, clerk.UserId, employee.Value.Id));
            Assert.True(assigned.IsSuccess, assigned.Error?.Description);
        });

        var login = await host.LoginAsync(clerk.Email, TestKeys.UserPassword);
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.AuthenticatedClient(login.AccessToken).GetAsync("/secure/orders-read")).StatusCode);

        var mux = host.Services.GetRequiredService<IConnectionMultiplexer>();
        var cacheKey = $"{host.RedisKeyPrefix}user:{clerk.UserId:D}";
        Assert.False((await mux.GetDatabase().StringGetAsync(cacheKey)).IsNullOrEmpty);

        await host.ExecuteScopedAsync(async sp =>
        {
            var removed = await sp.GetRequiredService<RemoveRoleFromUserUseCase>()
                .ExecuteAsync(new RemoveRoleFromUserCommand(owner.UserId, clerk.UserId, employee.Value.Id));
            Assert.True(removed.IsSuccess, removed.Error?.Description);
        });

        var cachedAfterRemoval = await mux.GetDatabase().StringGetAsync(cacheKey);
        Assert.False(cachedAfterRemoval.IsNull);

        var denied = await host.AuthenticatedClient(login.AccessToken).GetAsync("/secure/orders-read");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
