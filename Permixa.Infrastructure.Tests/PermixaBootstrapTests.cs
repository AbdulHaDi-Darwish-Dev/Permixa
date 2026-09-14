using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Login;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Register;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.EffectivePermissions;
using Permixa.Domain.Authorization;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.Infrastructure.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class PermixaBootstrapTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServer;
    private string _connectionString = null!;

    public PermixaBootstrapTests(SqlServerContainerFixture sqlServer)
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
    public async Task EmptyDatabase_Bootstrap_CreatesOwnerAuthorizationGraph()
    {
        await using var sp = BuildProvider(enabled: true);
        await MigrateAsync(sp);

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>()
                .BootstrapAsync();
        }

        using var assertScope = sp.CreateScope();
        await AssertBootstrapStateAsync(
            assertScope.ServiceProvider,
            expectPassword: "OwnerPass1!",
            expectedRbacVersion: 2);
    }

    [Fact]
    public async Task Bootstrap_UsesIdentityPasswordHashing()
    {
        await using var sp = BuildProvider(enabled: true, password: "OwnerPass1!");
        await MigrateAsync(sp);

        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = await userManager.FindByEmailAsync("owner@permixa.test");
        Assert.NotNull(owner);
        Assert.True(await userManager.CheckPasswordAsync(owner, "OwnerPass1!"));
    }

    [Fact]
    public async Task Bootstrap_IsIdempotent_WhenRunTwice()
    {
        await using var sp = BuildProvider(enabled: true);
        await MigrateAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var bootstrapper = scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>();
            await bootstrapper.BootstrapAsync();
            await bootstrapper.BootstrapAsync();
        }

        using var assertScope = sp.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = assertScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = assertScope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, await db.Roles.CountAsync(r => r.Name == SystemRoles.Owner));
        Assert.Equal(IamPermissions.All.Count, await db.Permissions.CountAsync());
        Assert.Equal(IamPermissions.All.Count, await db.RolePermissions.CountAsync());
        Assert.Equal(1, await db.AuthorizationStates.CountAsync());
        Assert.Equal(1, await db.UserRoles.CountAsync());

        var owner = await userManager.FindByEmailAsync("owner@permixa.test");
        Assert.NotNull(owner);
        Assert.True(owner.EmailConfirmed);
        Assert.Equal(ApplicationUser.InitialAuthorizationVersion, owner.AuthorizationVersion);

        var state = await db.AuthorizationStates.SingleAsync();
        Assert.Equal(2, state.RbacVersion);

        var role = await roleManager.FindByNameAsync(SystemRoles.Owner);
        Assert.NotNull(role);
        Assert.Equal(SystemRoles.OwnerRoleLevel, role.RoleLevel);
    }

    [Fact]
    public async Task Bootstrap_NewOwner_HasEmailConfirmedTrue()
    {
        await using var sp = BuildProvider(enabled: true);
        await MigrateAsync(sp);

        using (var scope = sp.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();

        using var assertScope = sp.CreateScope();
        var owner = await assertScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync("owner@permixa.test");
        Assert.NotNull(owner);
        Assert.True(owner.EmailConfirmed);
    }

    [Fact]
    public async Task Bootstrap_WithRequireConfirmedEmail_OwnerCanLogin()
    {
        await using var sp = BuildAuthProvider(requireConfirmedEmail: true);
        await MigrateAsync(sp);

        using (var scope = sp.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();

        using var scope2 = sp.CreateScope();
        var login = await scope2.ServiceProvider.GetRequiredService<LoginUseCase>()
            .ExecuteAsync(new LoginRequest
            {
                EmailOrUserName = "owner@permixa.test",
                Password = "OwnerPass1!"
            });
        Assert.True(login.IsSuccess, login.Error?.Description);
        Assert.True(login.Value.IsAuthenticated);
        Assert.NotNull(login.Value.Authentication);
        Assert.False(string.IsNullOrWhiteSpace(login.Value.Authentication.AccessToken));
    }

    [Fact]
    public async Task Bootstrap_WithRequireConfirmedEmail_RegisteredUserStillRequiresConfirmation()
    {
        await using var sp = BuildAuthProvider(requireConfirmedEmail: true);
        await MigrateAsync(sp);

        using (var scope = sp.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();

        using (var scope = sp.CreateScope())
        {
            var registered = await scope.ServiceProvider.GetRequiredService<RegisterUserUseCase>()
                .ExecuteAsync(new RegisterRequest
                {
                    UserName = "alice",
                    Email = "alice@permixa.test",
                    Password = "Passw0rd!"
                });
            Assert.True(registered.IsSuccess, registered.Error?.Description);
            Assert.False(registered.Value.EmailConfirmed);

            var login = await scope.ServiceProvider.GetRequiredService<LoginUseCase>()
                .ExecuteAsync(new LoginRequest
                {
                    EmailOrUserName = "alice@permixa.test",
                    Password = "Passw0rd!"
                });
            Assert.True(login.IsFailure);
            Assert.Equal(AuthenticationErrors.EmailNotConfirmed.Code, login.Error!.Code);
        }
    }

    [Fact]
    public async Task Bootstrap_DoesNotRepairExistingOwnerEmailConfirmed()
    {
        await using var sp = BuildProvider(enabled: true);
        await MigrateAsync(sp);

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var owner = await users.FindByEmailAsync("owner@permixa.test");
            Assert.NotNull(owner);
            owner.EmailConfirmed = false;
            Assert.Equal(IdentityResult.Success, await users.UpdateAsync(owner));
        }

        using (var scope = sp.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();

        using var assertScope = sp.CreateScope();
        var reloaded = await assertScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync("owner@permixa.test");
        Assert.NotNull(reloaded);
        Assert.False(reloaded.EmailConfirmed);
    }

    [Fact]
    public async Task DisabledBootstrap_DoesNotCreatePrivilegedState()
    {
        await using var sp = BuildProvider(enabled: false);
        await MigrateAsync(sp);

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>()
                .BootstrapAsync();
        }

        using var assertScope = sp.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Equal(0, await db.Roles.CountAsync());
        Assert.Equal(0, await db.Permissions.CountAsync());
        Assert.Equal(0, await db.AuthorizationStates.CountAsync());
    }

    [Fact]
    public async Task InvalidPassword_FailsAndLeavesNoPrivilegedState()
    {
        await using var sp = BuildProvider(enabled: true, password: "short");
        await MigrateAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync());
            Assert.Contains("Owner user", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("short", ex.Message, StringComparison.Ordinal);
        }

        using var assertScope = sp.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Equal(0, await db.Roles.CountAsync());
        Assert.Equal(0, await db.Permissions.CountAsync());
        Assert.Equal(0, await db.RolePermissions.CountAsync());
        Assert.Equal(0, await db.AuthorizationStates.CountAsync());
    }

    [Fact]
    public async Task OwnerRoleWithIncompatibleLevel_FailsSafely()
    {
        await using var sp = BuildProvider(enabled: true);
        await MigrateAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var conflict = new ApplicationRole(SystemRoles.Owner, 20);
            Assert.Equal(IdentityResult.Success, await roleManager.CreateAsync(conflict));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync());
            Assert.Contains("RoleLevel 20", ex.Message, StringComparison.Ordinal);
            Assert.Contains("expected 1", ex.Message, StringComparison.Ordinal);
        }

        using var assertScope = sp.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == SystemRoles.Owner);
        Assert.Equal(20, role.RoleLevel);
        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Fact]
    public async Task ExistingCorrectOwner_IsIdempotent_WithoutResettingVersions()
    {
        await using var sp = BuildProvider(enabled: true);
        await MigrateAsync(sp);

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
        }

        int rbacAfterFirst;
        int userVersionAfterFirst;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            rbacAfterFirst = (await db.AuthorizationStates.SingleAsync()).RbacVersion;
            var owner = await userManager.FindByEmailAsync("owner@permixa.test");
            Assert.NotNull(owner);
            userVersionAfterFirst = owner.AuthorizationVersion;
        }

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
        }

        using var assertScope = sp.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var assertUsers = assertScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal(rbacAfterFirst, (await assertDb.AuthorizationStates.SingleAsync()).RbacVersion);
        var ownerAgain = await assertUsers.FindByEmailAsync("owner@permixa.test");
        Assert.NotNull(ownerAgain);
        Assert.Equal(userVersionAfterFirst, ownerAgain.AuthorizationVersion);
    }

    [Fact]
    public async Task ExistingNonOwnerEmail_FailsClosed_WithoutElevation()
    {
        await using var sp = BuildProvider(enabled: true);
        await MigrateAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var existing = new ApplicationUser("regular.user")
            {
                Email = "owner@permixa.test"
            };
            Assert.Equal(IdentityResult.Success, await userManager.CreateAsync(existing, "OwnerPass1!"));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync());
            Assert.Contains("refused to elevate", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        using var assertScope = sp.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager2 = assertScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(0, await db.Roles.CountAsync(r => r.Name == SystemRoles.Owner));
        var user = await userManager2.FindByEmailAsync("owner@permixa.test");
        Assert.NotNull(user);
        Assert.Empty(await userManager2.GetRolesAsync(user));
    }

    [Fact]
    public async Task OwnerPermissions_ComeFromPersistedRolePermissions_NotBypass()
    {
        await using var sp = BuildProvider(enabled: true);
        await MigrateAsync(sp);

        Guid ownerId;
        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var owner = await userManager.FindByEmailAsync("owner@permixa.test");
            Assert.NotNull(owner);
            ownerId = owner.Id;
        }

        using var assertScope = sp.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var effective = assertScope.ServiceProvider.GetRequiredService<IEffectivePermissionService>();
        var permissionRepo = assertScope.ServiceProvider.GetRequiredService<IPermissionRepository>();

        var role = await db.Roles.SingleAsync(r => r.Name == SystemRoles.Owner);
        var rolePermissionNames = await (
            from rp in db.RolePermissions
            join p in db.Permissions on rp.PermissionId equals p.Id
            where rp.RoleId == role.Id
            select p.Name).ToListAsync();

        Assert.Equal(IamPermissions.All.Count, rolePermissionNames.Count);
        foreach (var name in IamPermissions.All)
            Assert.Contains(name, rolePermissionNames);

        var snapshot = await effective.GetAuthorizationSnapshotAsync(ownerId);
        Assert.True(snapshot.IsSuccess);
        foreach (var name in IamPermissions.All)
            Assert.True(snapshot.Value.HasPermission(name), $"Missing effective permission {name}");

        // Ensure EffectivePermissionService still requires persisted graph — removing RolePermissions removes access.
        db.RolePermissions.RemoveRange(db.RolePermissions);
        await db.SaveChangesAsync();

        // RbacVersion unchanged here intentionally; clear cache by bumping version for a realistic invalidation.
        var state = await db.AuthorizationStates.SingleAsync();
        state.IncrementRbacVersion();
        await db.SaveChangesAsync();

        var afterRemoval = await effective.GetAuthorizationSnapshotAsync(ownerId);
        Assert.True(afterRemoval.IsSuccess);
        Assert.Empty(afterRemoval.Value.Permissions);

        // Catalog still exists; only RolePermission edges were removed.
        Assert.Equal(IamPermissions.All.Count, (await permissionRepo.GetAllAsync()).Count);
    }

    [Fact]
    public async Task UsernameMismatch_DoesNotFail_ForCorrectOwner()
    {
        await using var sp = BuildProvider(
            enabled: true,
            userName: "configured-owner-name");
        await MigrateAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var role = new ApplicationRole(SystemRoles.Owner, SystemRoles.OwnerRoleLevel);
            Assert.Equal(IdentityResult.Success, await roleManager.CreateAsync(role));

            var user = new ApplicationUser("existing-owner-username")
            {
                Email = "owner@permixa.test"
            };
            Assert.Equal(IdentityResult.Success, await userManager.CreateAsync(user, "OwnerPass1!"));
            Assert.Equal(IdentityResult.Success, await userManager.AddToRoleAsync(user, SystemRoles.Owner));
        }

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
        }

        using var assertScope = sp.CreateScope();
        var users = assertScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = await users.FindByEmailAsync("owner@permixa.test");
        Assert.NotNull(owner);
        Assert.Equal("existing-owner-username", owner.UserName);
        Assert.True(await users.IsInRoleAsync(owner, SystemRoles.Owner));
        Assert.Equal(IamPermissions.All.Count,
            await assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().RolePermissions.CountAsync());
    }

    private ServiceProvider BuildProvider(
        bool enabled,
        string password = "OwnerPass1!",
        string userName = "permixa.owner")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString = _connectionString;
            o.Bootstrap.Enabled = enabled;
            o.Bootstrap.OwnerEmail = "owner@permixa.test";
            o.Bootstrap.OwnerUserName = userName;
            o.Bootstrap.OwnerPassword = password;
        });
        return services.BuildServiceProvider();
    }

    private ServiceProvider BuildAuthProvider(bool requireConfirmedEmail)
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
        services.AddPermixaAuthentication(o =>
        {
            o.Authentication.RequireConfirmedEmail = requireConfirmedEmail;
            o.Jwt.Issuer = TestJwtKeys.Issuer;
            o.Jwt.Audience = TestJwtKeys.Audience;
            o.Jwt.PrivateKeyPem = TestJwtKeys.PrivateKeyPem;
            o.Jwt.AccessTokenLifetime = TimeSpan.FromMinutes(15);
            o.Authentication.RefreshTokenLifetime = TimeSpan.FromDays(7);
        });
        return services.BuildServiceProvider();
    }

    private static async Task MigrateAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    private static async Task AssertBootstrapStateAsync(
        IServiceProvider sp,
        string expectPassword,
        int expectedRbacVersion)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var effective = sp.GetRequiredService<IEffectivePermissionService>();

        var state = await db.AuthorizationStates.SingleAsync(s => s.Id == AuthorizationState.GlobalId);
        Assert.Equal(expectedRbacVersion, state.RbacVersion);

        var role = await roleManager.FindByNameAsync(SystemRoles.Owner);
        Assert.NotNull(role);
        Assert.Equal(SystemRoles.OwnerRoleLevel, role.RoleLevel);

        var owner = await userManager.FindByEmailAsync("owner@permixa.test");
        Assert.NotNull(owner);
        Assert.True(owner.EmailConfirmed);
        Assert.True(await userManager.IsInRoleAsync(owner, SystemRoles.Owner));
        Assert.True(await userManager.CheckPasswordAsync(owner, expectPassword));

        var permissionNames = await db.Permissions.Select(p => p.Name).ToListAsync();
        Assert.Contains(IamPermissions.Permissions.Update, permissionNames);
        Assert.Contains(IamPermissions.Users.Read, permissionNames);
        Assert.Contains(IamPermissions.Users.Create, permissionNames);
        Assert.Contains(IamPermissions.Users.Lock, permissionNames);
        Assert.Contains(IamPermissions.Users.Disable, permissionNames);
        Assert.Contains(IamPermissions.Users.ChangeEmail, permissionNames);
        Assert.Contains(IamPermissions.Users.ForcePasswordReset, permissionNames);
        Assert.Contains(IamPermissions.Sessions.Read, permissionNames);
        Assert.Contains(IamPermissions.Sessions.Revoke, permissionNames);
        foreach (var name in IamPermissions.All)
            Assert.Contains(name, permissionNames);

        Assert.Equal(IamPermissions.All.Count, await db.RolePermissions.CountAsync(rp => rp.RoleId == role.Id));

        var snapshot = await effective.GetAuthorizationSnapshotAsync(owner.Id);
        Assert.True(snapshot.IsSuccess);
        foreach (var name in IamPermissions.All)
            Assert.True(snapshot.Value.HasPermission(name));
    }
}
