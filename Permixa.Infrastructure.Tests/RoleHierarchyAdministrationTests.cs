using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Permissions.Create;
using Permixa.Application.Authorization.RolePermissions.Assign;
using Permixa.Application.Authorization.Roles.ChangePosition;
using Permixa.Application.Authorization.Roles.Create;
using Permixa.Application.Authorization.Roles.Delete;
using Permixa.Application.Authorization.Roles.Get;
using Permixa.Application.Authorization.UserRoles.Assign;
using Permixa.Application.Authorization.UserRoles.Get;
using Permixa.Application.Authorization.UserRoles.Remove;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authorization;
using Permixa.Infrastructure;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.Infrastructure.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class RoleHierarchyAdministrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServer;
    private string _connectionString = null!;

    public RoleHierarchyAdministrationTests(SqlServerContainerFixture sqlServer)
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
    public async Task Placement_SameLevel_BelowFree_AboveAndCollision_AndExplicitSplit()
    {
        await using var sp = BuildProvider();
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);

        Guid adminId, managerId, supervisorId, leadId, employeeId;
        using (var scope = sp.CreateScope())
        {
            adminId = await SeedRoleAsync(scope.ServiceProvider, "Admin", 10);
            managerId = await SeedRoleAsync(scope.ServiceProvider, "Manager", 10);
            supervisorId = await SeedRoleAsync(scope.ServiceProvider, "Supervisor", 11);
            leadId = await SeedRoleAsync(scope.ServiceProvider, "Lead", 12);
            employeeId = await SeedRoleAsync(scope.ServiceProvider, "Employee", 20);
        }

        using (var scope = sp.CreateScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<CreateRoleUseCase>()
                .ExecuteAsync(new CreateRoleCommand(ownerId, "Auditor", managerId, RolePlacement.SameLevel));
            Assert.True(created.IsSuccess, created.Error?.Description);
            Assert.Equal(10, created.Value.RoleLevel);
        }

        using (var scope = sp.CreateScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<CreateRoleUseCase>()
                .ExecuteAsync(new CreateRoleCommand(ownerId, "Coordinator", employeeId, RolePlacement.Below));
            Assert.True(created.IsSuccess, created.Error?.Description);
            Assert.Equal(21, created.Value.RoleLevel);
        }

        using (var scope = sp.CreateScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<CreateRoleUseCase>()
                .ExecuteAsync(new CreateRoleCommand(ownerId, "NewBelowAdmin", adminId, RolePlacement.Below));
            Assert.True(created.IsSuccess, created.Error?.Description);
            Assert.Equal(11, created.Value.RoleLevel);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var levels = await db.Roles.ToDictionaryAsync(r => r.Name!, r => r.RoleLevel);
            Assert.Equal(1, levels[PermixaRoles.Owner]);
            Assert.Equal(10, levels["Admin"]);
            Assert.Equal(10, levels["Manager"]);
            Assert.Equal(10, levels["Auditor"]);
            Assert.Equal(11, levels["NewBelowAdmin"]);
            Assert.Equal(12, levels["Supervisor"]);
            Assert.Equal(13, levels["Lead"]);
            Assert.Equal(20, levels["Employee"]);
            Assert.Equal(21, levels["Coordinator"]);
        }

        using (var scope = sp.CreateScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<CreateRoleUseCase>()
                .ExecuteAsync(new CreateRoleCommand(ownerId, "TeamLead", managerId, RolePlacement.Above));
            Assert.True(created.IsSuccess, created.Error?.Description);
            Assert.Equal(10, created.Value.RoleLevel);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var levels = await db.Roles.ToDictionaryAsync(r => r.Name!, r => r.RoleLevel);
            Assert.Equal(1, levels[PermixaRoles.Owner]);
            Assert.Equal(10, levels["TeamLead"]);
            Assert.Equal(11, levels["Admin"]);
            Assert.Equal(11, levels["Manager"]);
            Assert.Equal(11, levels["Auditor"]);
            Assert.Equal(12, levels["NewBelowAdmin"]);
            Assert.Equal(13, levels["Supervisor"]);
            Assert.Equal(14, levels["Lead"]);
            Assert.Equal(20, levels["Employee"]);
        }

        using (var scope = sp.CreateScope())
        {
            var moved = await scope.ServiceProvider.GetRequiredService<ChangeRolePositionUseCase>()
                .ExecuteAsync(new ChangeRolePositionCommand(ownerId, managerId, employeeId, RolePlacement.Below));
            Assert.True(moved.IsSuccess, moved.Error?.Description);
            Assert.Equal(21, moved.Value.RoleLevel);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(11, (await db.Roles.SingleAsync(r => r.Name == "Auditor")).RoleLevel);
            Assert.Equal(11, (await db.Roles.SingleAsync(r => r.Name == "Admin")).RoleLevel);
            Assert.Equal(21, (await db.Roles.SingleAsync(r => r.Name == "Manager")).RoleLevel);
            Assert.Equal(1, (await db.Roles.SingleAsync(r => r.Name == PermixaRoles.Owner)).RoleLevel);
            Assert.DoesNotContain(await db.Roles.Select(r => r.RoleLevel).ToListAsync(), level => level <= 0);
        }
    }

    [Fact]
    public async Task CreateRole_VersionRules_MatchApprovedPlacementCases()
    {
        await using var sp = BuildProvider();
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);
        int baseline;
        Guid adminId;
        Guid managerId;

        using (var scope = sp.CreateScope())
        {
            adminId = await SeedRoleAsync(scope.ServiceProvider, "Admin", 10);
            managerId = await SeedRoleAsync(scope.ServiceProvider, "Manager", 20);
            baseline = (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .AuthorizationStates.SingleAsync()).RbacVersion;
        }

        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<CreateRoleUseCase>()
                .ExecuteAsync(new CreateRoleCommand(ownerId, "Auditor", managerId, RolePlacement.SameLevel))).IsSuccess);
            Assert.Equal(baseline, await ReadRbacAsync(scope.ServiceProvider));
        }

        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<CreateRoleUseCase>()
                .ExecuteAsync(new CreateRoleCommand(ownerId, "Supervisor", adminId, RolePlacement.Below))).IsSuccess);
            Assert.Equal(baseline, await ReadRbacAsync(scope.ServiceProvider));
        }

        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<CreateRoleUseCase>()
                .ExecuteAsync(new CreateRoleCommand(ownerId, "TeamLead", managerId, RolePlacement.Above))).IsSuccess);
            Assert.Equal(baseline + 1, await ReadRbacAsync(scope.ServiceProvider));
        }

        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<CreateRoleUseCase>()
                .ExecuteAsync(new CreateRoleCommand(ownerId, "Lead", adminId, RolePlacement.Below))).IsSuccess);
            Assert.Equal(baseline + 2, await ReadRbacAsync(scope.ServiceProvider));
        }
    }

    [Fact]
    public async Task ChangeRolePosition_VersionRules_AndOwnerStaysOne()
    {
        await using var sp = BuildProvider();
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);
        Guid adminId, managerId, employeeId;
        int baseline;

        using (var scope = sp.CreateScope())
        {
            adminId = await SeedRoleAsync(scope.ServiceProvider, "Admin", 10);
            managerId = await SeedRoleAsync(scope.ServiceProvider, "Manager", 11);
            employeeId = await SeedRoleAsync(scope.ServiceProvider, "Employee", 20);
            baseline = await ReadRbacAsync(scope.ServiceProvider);
        }

        using (var scope = sp.CreateScope())
        {
            var noOp = await scope.ServiceProvider.GetRequiredService<ChangeRolePositionUseCase>()
                .ExecuteAsync(new ChangeRolePositionCommand(ownerId, managerId, managerId, RolePlacement.SameLevel));
            Assert.True(noOp.IsSuccess);
            Assert.Equal(baseline, await ReadRbacAsync(scope.ServiceProvider));
        }

        using (var scope = sp.CreateScope())
        {
            var moved = await scope.ServiceProvider.GetRequiredService<ChangeRolePositionUseCase>()
                .ExecuteAsync(new ChangeRolePositionCommand(ownerId, managerId, employeeId, RolePlacement.Below));
            Assert.True(moved.IsSuccess, moved.Error?.Description);
            Assert.Equal(baseline + 1, await ReadRbacAsync(scope.ServiceProvider));
        }

        using (var scope = sp.CreateScope())
        {
            var shifted = await scope.ServiceProvider.GetRequiredService<ChangeRolePositionUseCase>()
                .ExecuteAsync(new ChangeRolePositionCommand(ownerId, employeeId, adminId, RolePlacement.Below));
            Assert.True(shifted.IsSuccess, shifted.Error?.Description);
            Assert.Equal(baseline + 2, await ReadRbacAsync(scope.ServiceProvider));
            Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Roles.SingleAsync(r => r.Name == PermixaRoles.Owner)).RoleLevel);
        }
    }

    [Fact]
    public async Task UserRoles_AndDelete_FollowApprovedRules()
    {
        await using var sp = BuildProvider();
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);
        Guid managerId;
        Guid clerkId;
        int userVersionBefore;

        using (var scope = sp.CreateScope())
        {
            managerId = await SeedRoleAsync(scope.ServiceProvider, "Manager", 20);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var clerk = new ApplicationUser("clerk") { Email = "clerk@permixa.test" };
            Assert.Equal(IdentityResult.Success, await users.CreateAsync(clerk, "ClerkPass1!"));
            clerkId = clerk.Id;
            userVersionBefore = clerk.AuthorizationVersion;
        }

        using (var scope = sp.CreateScope())
        {
            var assign = await scope.ServiceProvider.GetRequiredService<AssignRoleToUserUseCase>()
                .ExecuteAsync(new AssignRoleToUserCommand(ownerId, clerkId, managerId));
            Assert.True(assign.IsSuccess, assign.Error?.Description);
            var again = await scope.ServiceProvider.GetRequiredService<AssignRoleToUserUseCase>()
                .ExecuteAsync(new AssignRoleToUserCommand(ownerId, clerkId, managerId));
            Assert.True(again.IsSuccess);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var clerk = await db.Users.SingleAsync(u => u.Id == clerkId);
            Assert.Equal(userVersionBefore + 1, clerk.AuthorizationVersion);
            Assert.Equal(
                AuthorizationState.InitialRbacVersion + 1,
                (await db.AuthorizationStates.SingleAsync()).RbacVersion);

            var mine = await scope.ServiceProvider.GetRequiredService<GetMyRolesUseCase>()
                .ExecuteAsync(new GetMyRolesQuery(clerkId));
            Assert.Contains(mine.Value, r => r.Id == managerId);

            var listed = await scope.ServiceProvider.GetRequiredService<GetUsersInRoleUseCase>()
                .ExecuteAsync(new GetUsersInRoleQuery(ownerId, managerId));
            Assert.Contains(listed.Value, u => u.UserId == clerkId);
        }

        using (var scope = sp.CreateScope())
        {
            var deleteOccupied = await scope.ServiceProvider.GetRequiredService<DeleteRoleUseCase>()
                .ExecuteAsync(new DeleteRoleCommand(ownerId, managerId));
            Assert.Equal(AuthorizationErrors.RoleHasUsers, deleteOccupied.Error);

            var ownerRoleId = (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Roles.SingleAsync(r => r.Name == PermixaRoles.Owner)).Id;
            var deleteOwner = await scope.ServiceProvider.GetRequiredService<DeleteRoleUseCase>()
                .ExecuteAsync(new DeleteRoleCommand(ownerId, ownerRoleId));
            Assert.Equal(AuthorizationErrors.OwnerProtected, deleteOwner.Error);

            var assignOwner = await scope.ServiceProvider.GetRequiredService<AssignRoleToUserUseCase>()
                .ExecuteAsync(new AssignRoleToUserCommand(ownerId, clerkId, ownerRoleId));
            Assert.Equal(AuthorizationErrors.OwnerProtected, assignOwner.Error);
        }

        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<RemoveRoleFromUserUseCase>()
                .ExecuteAsync(new RemoveRoleFromUserCommand(ownerId, clerkId, managerId))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<RemoveRoleFromUserUseCase>()
                .ExecuteAsync(new RemoveRoleFromUserCommand(ownerId, clerkId, managerId))).IsSuccess);
        }

        using (var scope = sp.CreateScope())
        {
            var permission = await scope.ServiceProvider.GetRequiredService<CreatePermissionUseCase>()
                .ExecuteAsync(new CreatePermissionCommand(ownerId, "Orders.Assign", null));
            Assert.True((await scope.ServiceProvider.GetRequiredService<AssignPermissionToRoleUseCase>()
                .ExecuteAsync(new AssignPermissionToRoleCommand(ownerId, managerId, permission.Value.Id))).IsSuccess);

            var deleteGranted = await scope.ServiceProvider.GetRequiredService<DeleteRoleUseCase>()
                .ExecuteAsync(new DeleteRoleCommand(ownerId, managerId));
            Assert.Equal(AuthorizationErrors.RoleHasPermissions, deleteGranted.Error);
        }
    }

    [Fact]
    public async Task HierarchyWrite_RollsBackPartialShift()
    {
        await using var sp = BuildProvider();
        await MigrateBootstrapAndGetOwnerAsync(sp);
        Guid adminId;

        using (var scope = sp.CreateScope())
        {
            adminId = await SeedRoleAsync(scope.ServiceProvider, "Admin", 10);
            await SeedRoleAsync(scope.ServiceProvider, "Supervisor", 11);
        }

        using (var scope = sp.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IIdentityRoleWriter>();
            var hierarchyLock = scope.ServiceProvider.GetRequiredService<IAuthorizationHierarchyWriteLock>();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                uow.ExecuteInTransactionAsync(async ct =>
                {
                    Assert.NotNull(await hierarchyLock.AcquireAsync(ct));
                    await writer.ShiftTiersAsync([11], excludeRoleId: null, ct);
                    await uow.SaveChangesAsync(ct);
                    throw new InvalidOperationException("boom");
                }));
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(11, (await db.Roles.SingleAsync(r => r.Name == "Supervisor")).RoleLevel);
            Assert.Equal(10, (await db.Roles.SingleAsync(r => r.Name == "Admin")).RoleLevel);
        }
    }

    [Fact]
    public async Task HierarchyLock_RequiresOpenTransaction()
    {
        await using var sp = BuildProvider();
        await MigrateBootstrapAndGetOwnerAsync(sp);

        using var scope = sp.CreateScope();
        var hierarchyLock = scope.ServiceProvider.GetRequiredService<IAuthorizationHierarchyWriteLock>();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => hierarchyLock.AcquireAsync());
        Assert.Contains("transaction", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetRoles_HidesOwnerAndPeers()
    {
        await using var sp = BuildProvider();
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);
        Guid adminUserId;

        using (var scope = sp.CreateScope())
        {
            var adminId = await SeedRoleAsync(scope.ServiceProvider, "Admin", 10);
            await SeedRoleAsync(scope.ServiceProvider, "Manager", 20);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = new ApplicationUser("admin.user") { Email = "admin.user@permixa.test" };
            Assert.Equal(IdentityResult.Success, await users.CreateAsync(admin, "AdminPass1!"));
            Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(admin, "Admin"));
            adminUserId = admin.Id;

            var catalog = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Permissions;
            var manage = await catalog.SingleAsync(p => p.Name == IamPermissions.Roles.Read);
            await scope.ServiceProvider.GetRequiredService<AssignPermissionToRoleUseCase>()
                .ExecuteAsync(new AssignPermissionToRoleCommand(ownerId, adminId, manage.Id));
        }

        using (var scope = sp.CreateScope())
        {
            var roles = await scope.ServiceProvider.GetRequiredService<GetRolesUseCase>()
                .ExecuteAsync(new GetRolesQuery(adminUserId));
            Assert.True(roles.IsSuccess);
            Assert.DoesNotContain(roles.Value, r => r.Name == PermixaRoles.Owner);
            Assert.DoesNotContain(roles.Value, r => r.Name == "Admin");
            Assert.Contains(roles.Value, r => r.Name == "Manager");
        }
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

    private static async Task<Guid> MigrateBootstrapAndGetOwnerAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
        var owner = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync("owner@permixa.test");
        return owner!.Id;
    }

    private static async Task<Guid> SeedRoleAsync(IServiceProvider sp, string name, int level)
    {
        var roles = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var role = new ApplicationRole(name, level);
        Assert.Equal(IdentityResult.Success, await roles.CreateAsync(role));
        return role.Id;
    }

    private static async Task<int> ReadRbacAsync(IServiceProvider sp) =>
        (await sp.GetRequiredService<ApplicationDbContext>().AuthorizationStates.SingleAsync()).RbacVersion;
}
