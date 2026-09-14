using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.EffectivePermissions;
using Permixa.Application.Authorization.Permissions.Create;
using Permixa.Application.Authorization.Permissions.Get;
using Permixa.Application.Authorization.Permissions.Update;
using Permixa.Application.Authorization.RolePermissions.Assign;
using Permixa.Application.Authorization.UserPermissionOverrides.Set;
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
public sealed class AuthorizationAdministrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServer;
    private string _connectionString = null!;

    public AuthorizationAdministrationTests(SqlServerContainerFixture sqlServer)
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
    public async Task Bootstrap_SeedsPermissionsUpdate_AndIsIdempotent()
    {
        await using var sp = BuildProvider();
        await MigrateAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var bootstrapper = scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>();
            await bootstrapper.BootstrapAsync();
            await bootstrapper.BootstrapAsync();
        }

        using var assert = sp.CreateScope();
        var db = assert.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = assert.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var effective = assert.ServiceProvider.GetRequiredService<IEffectivePermissionService>();

        Assert.Equal(1, await db.Permissions.CountAsync(p => p.Name == IamPermissions.Permissions.Update));
        Assert.Equal(IamPermissions.All.Count, await db.Permissions.CountAsync());
        Assert.Equal(IamPermissions.All.Count, await db.RolePermissions.CountAsync());
        Assert.Equal(2, (await db.AuthorizationStates.SingleAsync()).RbacVersion);

        var owner = await users.FindByEmailAsync("owner@permixa.test");
        Assert.NotNull(owner);
        var snapshot = await effective.GetAuthorizationSnapshotAsync(owner.Id);
        Assert.True(snapshot.IsSuccess);
        Assert.True(snapshot.Value.HasPermission(IamPermissions.Permissions.Update));
    }

    [Fact]
    public async Task UpdatePermissionDescription_Persists_WithoutVersionBumps()
    {
        await using var sp = BuildProvider();
        await MigrateAndBootstrapAsync(sp);

        Guid ownerId;
        Guid permissionId;
        int rbacBefore;
        int userVersionBefore;

        using (var scope = sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var owner = await users.FindByEmailAsync("owner@permixa.test");
            ownerId = owner!.Id;

            var created = await scope.ServiceProvider.GetRequiredService<CreatePermissionUseCase>()
                .ExecuteAsync(new CreatePermissionCommand(ownerId, "Orders.Read", "Original"));
            Assert.True(created.IsSuccess);
            permissionId = created.Value.Id;

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            rbacBefore = (await db.AuthorizationStates.SingleAsync()).RbacVersion;
            userVersionBefore = owner.AuthorizationVersion;
        }

        using (var scope = sp.CreateScope())
        {
            var updated = await scope.ServiceProvider.GetRequiredService<UpdatePermissionDescriptionUseCase>()
                .ExecuteAsync(new UpdatePermissionDescriptionCommand(ownerId, permissionId, "Changed"));
            Assert.True(updated.IsSuccess);
            Assert.Equal("Orders.Read", updated.Value.Name);
            Assert.Equal("Changed", updated.Value.Description);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var permission = await db.Permissions.SingleAsync(p => p.Id == permissionId);
            var owner = await db.Users.SingleAsync(u => u.Id == ownerId);

            Assert.Equal("Orders.Read", permission.Name);
            Assert.Equal("Changed", permission.Description);
            Assert.Equal(rbacBefore, (await db.AuthorizationStates.SingleAsync()).RbacVersion);
            Assert.Equal(userVersionBefore, owner.AuthorizationVersion);
        }
    }

    [Fact]
    public async Task GetEffectivePermissions_AdminAndSelf_HonorSecurityBoundary()
    {
        await using var sp = BuildProvider();
        await MigrateAndBootstrapAsync(sp);

        Guid ownerId;
        Guid clerkId;
        Guid permissionId;
        Guid clerkRoleId;
        using (var scope = sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            ownerId = (await users.FindByEmailAsync("owner@permixa.test"))!.Id;

            var clerkRole = new ApplicationRole("Clerk", 50);
            Assert.Equal(IdentityResult.Success, await roles.CreateAsync(clerkRole));
            clerkRoleId = clerkRole.Id;
            var clerk = new ApplicationUser("clerk") { Email = "clerk@permixa.test" };
            Assert.Equal(IdentityResult.Success, await users.CreateAsync(clerk, "ClerkPass1!"));
            Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(clerk, "Clerk"));
            clerkId = clerk.Id;

            var created = await scope.ServiceProvider.GetRequiredService<CreatePermissionUseCase>()
                .ExecuteAsync(new CreatePermissionCommand(ownerId, "Orders.Approve", null));
            Assert.True(created.IsSuccess);
            permissionId = created.Value.Id;

            Assert.True((await scope.ServiceProvider.GetRequiredService<AssignPermissionToRoleUseCase>()
                .ExecuteAsync(new AssignPermissionToRoleCommand(ownerId, clerkRoleId, permissionId))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<SetUserPermissionOverrideUseCase>()
                .ExecuteAsync(new SetUserPermissionOverrideCommand(
                    ownerId, clerkId, permissionId, PermissionEffect.Deny))).IsSuccess);
        }

        using var assert = sp.CreateScope();
        var admin = await assert.ServiceProvider.GetRequiredService<GetEffectivePermissionsUseCase>()
            .ExecuteAsync(new GetEffectivePermissionsQuery(ownerId, clerkId));
        Assert.True(admin.IsSuccess);
        Assert.DoesNotContain("Orders.Approve", admin.Value.Permissions);

        var self = await assert.ServiceProvider.GetRequiredService<GetMyEffectivePermissionsUseCase>()
            .ExecuteAsync(new GetMyEffectivePermissionsQuery(clerkId));
        Assert.True(self.IsSuccess);
        Assert.DoesNotContain("Orders.Approve", self.Value.Permissions);

        var clerkReadsCatalog = await assert.ServiceProvider.GetRequiredService<GetPermissionsUseCase>()
            .ExecuteAsync(new GetPermissionsQuery(clerkId));
        Assert.Equal(AuthorizationErrors.MissingManagePermission, clerkReadsCatalog.Error);
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

    private static async Task MigrateAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    private static async Task MigrateAndBootstrapAsync(ServiceProvider sp)
    {
        await MigrateAsync(sp);
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
    }
}
