using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.Login;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Refresh;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Permissions.Create;
using Permixa.Application.Authorization.RolePermissions.Assign;
using Permixa.Application.Authorization.UserPermissionOverrides.Set;
using Permixa.Application.Authorization.UserRoles.Assign;
using Permixa.Application.Authorization.Users.Create;
using Permixa.Application.Authorization.Users.Disable;
using Permixa.Application.Authorization.Users.Get;
using Permixa.Application.Authorization.Users.Lock;
using Permixa.Application.Common.Paging;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authentication;
using Permixa.Domain.Authorization;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Permixa.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.Infrastructure.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class UserAdministrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServer;
    private string _connectionString = null!;

    public UserAdministrationTests(SqlServerContainerFixture sqlServer)
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
    public async Task Migration_AddsIsDisabled_NotNullDefaultFalse()
    {
        await using var sp = BuildProvider();
        await MigrateAsync(sp);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT c.is_nullable, dc.definition
            FROM sys.columns c
            INNER JOIN sys.tables t ON t.object_id = c.object_id
            LEFT JOIN sys.default_constraints dc
                ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE t.name = N'AspNetUsers' AND c.name = N'IsDisabled'
            """;

        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.False(reader.GetBoolean(0));
            var defaultSql = reader.IsDBNull(1) ? null : reader.GetString(1);
            Assert.Contains("0", defaultSql, StringComparison.Ordinal);
        }

        await db.Database.CloseConnectionAsync();

        var user = new ApplicationUser("existing.baseline") { Email = "existing.baseline@permixa.test" };
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(user, "Passw0rd!"));
        Assert.False(user.IsDisabled);
    }

    [Fact]
    public async Task Bootstrap_SeedsUserAdministrationPermissions_ForOwner_Idempotently()
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

        foreach (var name in new[]
                 {
                     IamPermissions.Users.Read,
                     IamPermissions.Users.Create,
                     IamPermissions.Users.Lock,
                     IamPermissions.Users.Disable
                 })
        {
            Assert.Equal(1, await db.Permissions.CountAsync(p => p.Name == name));
        }

        Assert.Equal(IamPermissions.All.Count, await db.Permissions.CountAsync());
        Assert.Equal(IamPermissions.All.Count, await db.RolePermissions.CountAsync());
        Assert.Equal(2, (await db.AuthorizationStates.SingleAsync()).RbacVersion);

        var owner = await users.FindByEmailAsync("owner@permixa.test");
        Assert.NotNull(owner);
        var snapshot = await effective.GetAuthorizationSnapshotAsync(owner.Id);
        Assert.True(snapshot.IsSuccess);
        Assert.True(snapshot.Value.HasPermission(IamPermissions.Users.Read));
        Assert.True(snapshot.Value.HasPermission(IamPermissions.Users.Create));
        Assert.True(snapshot.Value.HasPermission(IamPermissions.Users.Lock));
        Assert.True(snapshot.Value.HasPermission(IamPermissions.Users.Disable));
    }

    [Fact]
    public async Task AdminCreateUser_CreatesIdentityOnly_WithoutRolesOrVersionBumps()
    {
        await using var sp = BuildProvider();
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);
        int rbacBefore;
        using (var scope = sp.CreateScope())
        {
            rbacBefore = await ReadRbacAsync(scope.ServiceProvider);
        }

        Guid createdId;
        using (var scope = sp.CreateScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<AdminCreateUserUseCase>()
                .ExecuteAsync(new AdminCreateUserCommand(
                    ownerId, "new.user@permixa.test", "new.user", "Passw0rd!"));
            Assert.True(created.IsSuccess, created.Error?.Description);
            Assert.False(created.Value.EmailConfirmed);
            Assert.False(created.Value.IsDisabled);
            Assert.False(created.Value.IsLocked);
            Assert.Null(created.Value.EffectiveRoleLevel);
            createdId = created.Value.Id;
        }

        using (var scope = sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var reader = scope.ServiceProvider.GetRequiredService<IIdentityUserReader>();
            var overrides = scope.ServiceProvider.GetRequiredService<IUserPermissionOverrideRepository>();
            var user = await users.FindByIdAsync(createdId.ToString());
            Assert.NotNull(user);
            Assert.False(user.EmailConfirmed);
            Assert.False(user.IsDisabled);
            Assert.Equal(ApplicationUser.InitialAuthorizationVersion, user.AuthorizationVersion);
            Assert.Empty(await reader.GetUserRoleIdsAsync(createdId));
            Assert.Empty(await overrides.GetByUserIdAsync(createdId));
            Assert.Equal(rbacBefore, await ReadRbacAsync(scope.ServiceProvider));
        }

        using (var scope = sp.CreateScope())
        {
            var duplicateEmail = await scope.ServiceProvider.GetRequiredService<AdminCreateUserUseCase>()
                .ExecuteAsync(new AdminCreateUserCommand(
                    ownerId, "new.user@permixa.test", "other.user", "Passw0rd!"));
            Assert.Equal(AuthenticationErrors.EmailAlreadyExists, duplicateEmail.Error);

            var duplicateName = await scope.ServiceProvider.GetRequiredService<AdminCreateUserUseCase>()
                .ExecuteAsync(new AdminCreateUserCommand(
                    ownerId, "other.user@permixa.test", "new.user", "Passw0rd!"));
            Assert.Equal(AuthenticationErrors.UserNameAlreadyExists, duplicateName.Error);
        }
    }

    [Fact]
    public async Task AdminCreateUser_MissingPermission_IsForbidden()
    {
        await using var sp = BuildProvider();
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);
        var clerkId = await CreateUserAsync(sp, "clerk.create", "clerk.create@permixa.test");

        using var scope = sp.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<AdminCreateUserUseCase>()
            .ExecuteAsync(new AdminCreateUserCommand(
                clerkId, "blocked@permixa.test", "blocked", "Passw0rd!"));
        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
        Assert.NotEqual(ownerId, clerkId);
    }

    [Fact]
    public async Task GetUser_SecurityMatrix_AndDetailsAggregate()
    {
        await using var sp = BuildProvider();
        var ctx = await SeedDirectoryAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var getById = scope.ServiceProvider.GetRequiredService<GetUserByIdUseCase>();
            var getByEmail = scope.ServiceProvider.GetRequiredService<GetUserByEmailUseCase>();
            var details = scope.ServiceProvider.GetRequiredService<GetUserIamDetailsUseCase>();

            var ok = await getById.ExecuteAsync(new GetUserByIdQuery(ctx.AdminUserId, ctx.ClerkUserId));
            Assert.True(ok.IsSuccess, ok.Error?.Description);
            Assert.Equal("clerk.user", ok.Value.UserName);

            var byEmail = await getByEmail.ExecuteAsync(
                new GetUserByEmailQuery(ctx.AdminUserId, "clerk.user@permixa.test"));
            Assert.True(byEmail.IsSuccess, byEmail.Error?.Description);
            Assert.Equal(ctx.ClerkUserId, byEmail.Value.Id);

            var missingPermission = await getById.ExecuteAsync(
                new GetUserByIdQuery(ctx.ClerkUserId, ctx.NoRoleUserId));
            Assert.Equal(AuthorizationErrors.MissingManagePermission, missingPermission.Error);

            var peer = await getById.ExecuteAsync(new GetUserByIdQuery(ctx.AdminUserId, ctx.PeerAdminUserId));
            Assert.Equal(AuthorizationErrors.HierarchyViolation, peer.Error);

            var owner = await getById.ExecuteAsync(new GetUserByIdQuery(ctx.AdminUserId, ctx.OwnerId));
            Assert.Equal(AuthorizationErrors.HierarchyViolation, owner.Error);

            var missing = await getById.ExecuteAsync(new GetUserByIdQuery(ctx.AdminUserId, Guid.NewGuid()));
            Assert.Equal(AuthorizationErrors.UserNotFound, missing.Error);

            var selfDetails = await details.ExecuteAsync(
                new GetUserIamDetailsQuery(ctx.AdminUserId, ctx.AdminUserId));
            Assert.Equal(AuthorizationErrors.CannotManageSelf, selfDetails.Error);

            var aggregate = await details.ExecuteAsync(
                new GetUserIamDetailsQuery(ctx.AdminUserId, ctx.ClerkUserId));
            Assert.True(aggregate.IsSuccess, aggregate.Error?.Description);
            Assert.Equal(ctx.ClerkLevel, aggregate.Value.User.EffectiveRoleLevel);
            Assert.Contains(aggregate.Value.Roles, r => r.Name == "Clerk");
            Assert.Contains(aggregate.Value.Overrides, o => o.PermissionName == "Orders.Approve" && o.Effect == PermissionEffect.Deny);
            Assert.DoesNotContain("Orders.Approve", aggregate.Value.EffectivePermissions);
            Assert.Contains("Orders.View", aggregate.Value.EffectivePermissions);
        }
    }

    [Fact]
    public async Task GetUsers_PagesFiltersAndHonorsHierarchy()
    {
        await using var sp = BuildProvider();
        var ctx = await SeedDirectoryAsync(sp);

        using var scope = sp.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<GetUsersUseCase>();

        var page1 = await sut.ExecuteAsync(new GetUsersQuery(ctx.AdminUserId, new PageRequest(1, 1)));
        Assert.True(page1.IsSuccess, page1.Error?.Description);
        Assert.Single(page1.Value.Items);
        Assert.True(page1.Value.TotalCount >= 2);

        var page2 = await sut.ExecuteAsync(new GetUsersQuery(ctx.AdminUserId, new PageRequest(2, 1)));
        Assert.True(page2.IsSuccess);
        Assert.Single(page2.Value.Items);
        Assert.NotEqual(page1.Value.Items[0].Id, page2.Value.Items[0].Id);

        var all = await sut.ExecuteAsync(new GetUsersQuery(ctx.AdminUserId, new PageRequest(1, 50)));
        Assert.True(all.IsSuccess);
        var ids = all.Value.Items.Select(u => u.Id).ToHashSet();
        Assert.Contains(ctx.ClerkUserId, ids);
        Assert.Contains(ctx.NoRoleUserId, ids);
        Assert.DoesNotContain(ctx.AdminUserId, ids);
        Assert.DoesNotContain(ctx.PeerAdminUserId, ids);
        Assert.DoesNotContain(ctx.OwnerId, ids);

        var ownerList = await sut.ExecuteAsync(new GetUsersQuery(ctx.OwnerId, new PageRequest(1, 50)));
        Assert.True(ownerList.IsSuccess, ownerList.Error?.Description);
        var ownerIds = ownerList.Value.Items.Select(u => u.Id).ToHashSet();
        Assert.Contains(ctx.AdminUserId, ownerIds);
        Assert.Contains(ctx.ClerkUserId, ownerIds);
        Assert.DoesNotContain(ctx.OwnerId, ownerIds);

        var ordered = all.Value.Items.Select(u => (u.UserName, u.Id)).ToList();
        var expected = ordered.OrderBy(x => x.UserName, StringComparer.Ordinal)
            .ThenBy(x => x.Id)
            .ToList();
        Assert.Equal(expected, ordered);

        var searchEmail = await sut.ExecuteAsync(
            new GetUsersQuery(ctx.AdminUserId, new PageRequest(), Search: "clerk.user@"));
        Assert.True(searchEmail.IsSuccess);
        Assert.Contains(searchEmail.Value.Items, u => u.Id == ctx.ClerkUserId);

        var searchName = await sut.ExecuteAsync(
            new GetUsersQuery(ctx.AdminUserId, new PageRequest(), Search: "norole.user"));
        Assert.True(searchName.IsSuccess);
        Assert.Contains(searchName.Value.Items, u => u.Id == ctx.NoRoleUserId);

        var disabled = await sut.ExecuteAsync(
            new GetUsersQuery(ctx.AdminUserId, new PageRequest(), IsDisabled: true));
        Assert.True(disabled.IsSuccess);
        Assert.Contains(disabled.Value.Items, u => u.Id == ctx.DisabledUserId);
        Assert.DoesNotContain(disabled.Value.Items, u => u.Id == ctx.ClerkUserId);

        var locked = await sut.ExecuteAsync(
            new GetUsersQuery(ctx.AdminUserId, new PageRequest(), IsLocked: true));
        Assert.True(locked.IsSuccess);
        Assert.Contains(locked.Value.Items, u => u.Id == ctx.LockedUserId);
    }

    [Fact]
    public async Task LockUnlock_SecurityAndIdentityState()
    {
        await using var sp = BuildProvider();
        var ctx = await SeedDirectoryAsync(sp);
        var until = DateTime.UtcNow.AddHours(3);
        var earlier = DateTime.UtcNow.AddHours(1);

        using (var scope = sp.CreateScope())
        {
            var lockUser = scope.ServiceProvider.GetRequiredService<LockUserUseCase>();
            Assert.Equal(
                AuthorizationErrors.InvalidLockoutEnd,
                (await lockUser.ExecuteAsync(new LockUserCommand(ctx.AdminUserId, ctx.ClerkUserId, DateTime.UtcNow.AddMinutes(-1)))).Error);

            Assert.Equal(
                AuthorizationErrors.MissingManagePermission,
                (await lockUser.ExecuteAsync(new LockUserCommand(ctx.ClerkUserId, ctx.NoRoleUserId, until))).Error);

            Assert.Equal(
                AuthorizationErrors.CannotManageSelf,
                (await lockUser.ExecuteAsync(new LockUserCommand(ctx.AdminUserId, ctx.AdminUserId, until))).Error);

            Assert.Equal(
                AuthorizationErrors.OwnerProtected,
                (await lockUser.ExecuteAsync(new LockUserCommand(ctx.AdminUserId, ctx.OwnerId, until))).Error);

            Assert.Equal(
                AuthorizationErrors.HierarchyViolation,
                (await lockUser.ExecuteAsync(new LockUserCommand(ctx.AdminUserId, ctx.PeerAdminUserId, until))).Error);

            var locked = await lockUser.ExecuteAsync(new LockUserCommand(ctx.AdminUserId, ctx.ClerkUserId, until));
            Assert.True(locked.IsSuccess, locked.Error?.Description);

            var idempotent = await lockUser.ExecuteAsync(new LockUserCommand(ctx.AdminUserId, ctx.ClerkUserId, earlier));
            Assert.True(idempotent.IsSuccess, idempotent.Error?.Description);
        }

        using (var scope = sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var clerk = await users.FindByIdAsync(ctx.ClerkUserId.ToString());
            Assert.NotNull(clerk);
            Assert.True(clerk.LockoutEnabled);
            Assert.True(clerk.LockoutEnd >= new DateTimeOffset(DateTime.SpecifyKind(until, DateTimeKind.Utc)).AddMinutes(-1));
            clerk.AccessFailedCount = 3;
            await users.UpdateAsync(clerk);
        }

        using (var scope = sp.CreateScope())
        {
            var unlock = scope.ServiceProvider.GetRequiredService<UnlockUserUseCase>();
            var unlocked = await unlock.ExecuteAsync(new UnlockUserCommand(ctx.AdminUserId, ctx.ClerkUserId));
            Assert.True(unlocked.IsSuccess, unlocked.Error?.Description);

            var again = await unlock.ExecuteAsync(new UnlockUserCommand(ctx.AdminUserId, ctx.ClerkUserId));
            Assert.True(again.IsSuccess);

            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var clerk = await users.FindByIdAsync(ctx.ClerkUserId.ToString());
            Assert.Null(clerk!.LockoutEnd);
            Assert.Equal(0, clerk.AccessFailedCount);
            Assert.True(clerk.LockoutEnabled);
        }
    }

    [Fact]
    public async Task DisableEnable_RevokesRefreshTokens_WithoutVersionBumps()
    {
        await using var sp = BuildProvider();
        var ctx = await SeedDirectoryAsync(sp);
        var login1 = await LoginAsync(sp, "norole.user@permixa.test", "Passw0rd!");
        var login2 = await LoginAsync(sp, "norole.user@permixa.test", "Passw0rd!");

        int rbacBefore;
        int authzBefore;
        using (var scope = sp.CreateScope())
        {
            rbacBefore = await ReadRbacAsync(scope.ServiceProvider);
            authzBefore = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByIdAsync(ctx.NoRoleUserId.ToString()))!.AuthorizationVersion;
        }

        using (var scope = sp.CreateScope())
        {
            var disable = scope.ServiceProvider.GetRequiredService<DisableUserUseCase>();
            Assert.Equal(
                AuthorizationErrors.CannotManageSelf,
                (await disable.ExecuteAsync(new DisableUserCommand(ctx.AdminUserId, ctx.AdminUserId))).Error);
            Assert.Equal(
                AuthorizationErrors.OwnerProtected,
                (await disable.ExecuteAsync(new DisableUserCommand(ctx.AdminUserId, ctx.OwnerId))).Error);
            Assert.Equal(
                AuthorizationErrors.HierarchyViolation,
                (await disable.ExecuteAsync(new DisableUserCommand(ctx.AdminUserId, ctx.PeerAdminUserId))).Error);
            Assert.Equal(
                AuthorizationErrors.MissingManagePermission,
                (await disable.ExecuteAsync(new DisableUserCommand(ctx.ClerkUserId, ctx.NoRoleUserId))).Error);

            var disabled = await disable.ExecuteAsync(new DisableUserCommand(ctx.AdminUserId, ctx.NoRoleUserId));
            Assert.True(disabled.IsSuccess, disabled.Error?.Description);
            var again = await disable.ExecuteAsync(new DisableUserCommand(ctx.AdminUserId, ctx.NoRoleUserId));
            Assert.True(again.IsSuccess);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == ctx.NoRoleUserId);
            Assert.True(user.IsDisabled);
            Assert.Equal(authzBefore, user.AuthorizationVersion);
            Assert.Equal(rbacBefore, await ReadRbacAsync(scope.ServiceProvider));
            Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.UserId == ctx.NoRoleUserId && t.RevokedAtUtc == null));

            var refresh = scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();
            var refreshDenied = await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = login1.RefreshToken });
            Assert.Equal(AuthenticationErrors.RefreshTokenRevoked.Code, refreshDenied.Error!.Code);

            var login = await scope.ServiceProvider.GetRequiredService<LoginUseCase>()
                .ExecuteAsync(new LoginRequest
                {
                    EmailOrUserName = "norole.user@permixa.test",
                    Password = "Passw0rd!"
                });
            Assert.Equal(AuthenticationErrors.InvalidCredentials.Code, login.Error!.Code);
        }

        using (var scope = sp.CreateScope())
        {
            var enable = scope.ServiceProvider.GetRequiredService<EnableUserUseCase>();
            var enabled = await enable.ExecuteAsync(new EnableUserCommand(ctx.AdminUserId, ctx.NoRoleUserId));
            Assert.True(enabled.IsSuccess, enabled.Error?.Description);
            var again = await enable.ExecuteAsync(new EnableUserCommand(ctx.AdminUserId, ctx.NoRoleUserId));
            Assert.True(again.IsSuccess);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == ctx.NoRoleUserId);
            Assert.False(user.IsDisabled);
            Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.UserId == ctx.NoRoleUserId && t.RevokedAtUtc == null));
            Assert.Equal(authzBefore, user.AuthorizationVersion);
            Assert.Equal(rbacBefore, await ReadRbacAsync(scope.ServiceProvider));

            var stillRevoked = await scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>()
                .ExecuteAsync(new RefreshTokenRequest { RefreshToken = login2.RefreshToken });
            Assert.Equal(AuthenticationErrors.RefreshTokenRevoked.Code, stillRevoked.Error!.Code);

            var login = await scope.ServiceProvider.GetRequiredService<LoginUseCase>()
                .ExecuteAsync(new LoginRequest
                {
                    EmailOrUserName = "norole.user@permixa.test",
                    Password = "Passw0rd!"
                });
            Assert.True(login.IsSuccess, login.Error?.Description);
        }
    }

    [Fact]
    public async Task Unlock_DoesNotEnableDisabledUser_AndEnableDoesNotUnlock()
    {
        await using var sp = BuildProvider();
        var ctx = await SeedDirectoryAsync(sp);
        var until = DateTime.UtcNow.AddHours(2);

        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<DisableUserUseCase>()
                .ExecuteAsync(new DisableUserCommand(ctx.AdminUserId, ctx.ClerkUserId))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<LockUserUseCase>()
                .ExecuteAsync(new LockUserCommand(ctx.AdminUserId, ctx.ClerkUserId, until))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<UnlockUserUseCase>()
                .ExecuteAsync(new UnlockUserCommand(ctx.AdminUserId, ctx.ClerkUserId))).IsSuccess);
        }

        using (var scope = sp.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByIdAsync(ctx.ClerkUserId.ToString());
            Assert.True(user!.IsDisabled);
            Assert.Null(user.LockoutEnd);
        }

        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<LockUserUseCase>()
                .ExecuteAsync(new LockUserCommand(ctx.AdminUserId, ctx.NoRoleUserId, until))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<EnableUserUseCase>()
                .ExecuteAsync(new EnableUserCommand(ctx.AdminUserId, ctx.NoRoleUserId))).IsSuccess);
        }

        using (var scope = sp.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByIdAsync(ctx.NoRoleUserId.ToString());
            Assert.False(user!.IsDisabled);
            Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow);
        }
    }

    [Fact]
    public async Task Disable_TransactionFailure_RollsBackAccountAndTokens()
    {
        await using var sp = BuildProvider(throwAfterRevoke: true);
        var ctx = await SeedDirectoryAsync(sp);
        await LoginAsync(sp, "norole.user@permixa.test", "Passw0rd!");

        using (var scope = sp.CreateScope())
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<DisableUserUseCase>()
                    .ExecuteAsync(new DisableUserCommand(ctx.AdminUserId, ctx.NoRoleUserId)));
            Assert.Contains("Forced failure", ex.Message, StringComparison.Ordinal);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == ctx.NoRoleUserId);
            Assert.False(user.IsDisabled);
            Assert.Equal(1, await db.RefreshTokens.CountAsync(t => t.UserId == ctx.NoRoleUserId && t.RevokedAtUtc == null));
        }
    }

    private ServiceProvider BuildProvider(bool throwAfterRevoke = false)
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
        services.AddPermixaAuthentication(o =>
        {
            o.Jwt.Issuer = TestJwtKeys.Issuer;
            o.Jwt.Audience = TestJwtKeys.Audience;
            o.Jwt.PrivateKeyPem = TestJwtKeys.PrivateKeyPem;
            o.Jwt.AccessTokenLifetime = TimeSpan.FromMinutes(15);
            o.Authentication.RefreshTokenLifetime = TimeSpan.FromDays(7);
        });

        if (throwAfterRevoke)
        {
            var existing = services.Single(d => d.ServiceType == typeof(IRefreshTokenRepository));
            services.Remove(existing);
            services.AddScoped<RefreshTokenRepository>();
            services.AddScoped<IRefreshTokenRepository>(sp =>
                new ThrowAfterRevokeRefreshTokenRepository(sp.GetRequiredService<RefreshTokenRepository>()));
        }

        return services.BuildServiceProvider();
    }

    private static async Task MigrateAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
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

    private static async Task<int> ReadRbacAsync(IServiceProvider sp) =>
        (await sp.GetRequiredService<ApplicationDbContext>().AuthorizationStates.SingleAsync()).RbacVersion;

    private static async Task<Guid> CreateUserAsync(ServiceProvider sp, string userName, string email)
    {
        using var scope = sp.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser(userName) { Email = email };
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(user, "Passw0rd!"));
        return user.Id;
    }

    private static async Task<LoginTokens> LoginAsync(ServiceProvider sp, string email, string password)
    {
        using var scope = sp.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<LoginUseCase>()
            .ExecuteAsync(new LoginRequest { EmailOrUserName = email, Password = password });
        Assert.True(result.IsSuccess, result.Error?.Description);
        Assert.True(result.Value.IsAuthenticated);
        return new LoginTokens(result.Value.Authentication!.AccessToken, result.Value.Authentication.RefreshToken);
    }

    private static async Task<DirectoryContext> SeedDirectoryAsync(ServiceProvider sp)
    {
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);
        Guid adminRoleId, clerkRoleId, adminUserId, peerAdminUserId, clerkUserId, noRoleUserId, disabledUserId, lockedUserId;
        const int adminLevel = 10;
        const int clerkLevel = 20;

        using (var scope = sp.CreateScope())
        {
            adminRoleId = await SeedRoleAsync(scope.ServiceProvider, "Admin", adminLevel);
            clerkRoleId = await SeedRoleAsync(scope.ServiceProvider, "Clerk", clerkLevel);
            await SeedRoleAsync(scope.ServiceProvider, "PeerAdmin", adminLevel);

            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await CreateNamedUserAsync(users, "admin.user", "admin.user@permixa.test");
            var peer = await CreateNamedUserAsync(users, "peer.admin", "peer.admin@permixa.test");
            var clerk = await CreateNamedUserAsync(users, "clerk.user", "clerk.user@permixa.test");
            var noRole = await CreateNamedUserAsync(users, "norole.user", "norole.user@permixa.test");
            var disabled = await CreateNamedUserAsync(users, "disabled.user", "disabled.user@permixa.test");
            var locked = await CreateNamedUserAsync(users, "locked.user", "locked.user@permixa.test");

            Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(admin, "Admin"));
            Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(peer, "PeerAdmin"));
            Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(clerk, "Clerk"));

            disabled.SetDisabled(true);
            Assert.Equal(IdentityResult.Success, await users.UpdateAsync(disabled));
            Assert.Equal(IdentityResult.Success, await users.SetLockoutEnabledAsync(locked, true));
            Assert.Equal(IdentityResult.Success, await users.SetLockoutEndDateAsync(locked, DateTimeOffset.UtcNow.AddHours(2)));

            adminUserId = admin.Id;
            peerAdminUserId = peer.Id;
            clerkUserId = clerk.Id;
            noRoleUserId = noRole.Id;
            disabledUserId = disabled.Id;
            lockedUserId = locked.Id;

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var name in new[]
                     {
                         IamPermissions.Users.Read,
                         IamPermissions.Users.Create,
                         IamPermissions.Users.Lock,
                         IamPermissions.Users.Disable
                     })
            {
                var permission = await db.Permissions.SingleAsync(p => p.Name == name);
                Assert.True((await scope.ServiceProvider.GetRequiredService<AssignPermissionToRoleUseCase>()
                    .ExecuteAsync(new AssignPermissionToRoleCommand(ownerId, adminRoleId, permission.Id))).IsSuccess);
            }

            var view = await scope.ServiceProvider.GetRequiredService<CreatePermissionUseCase>()
                .ExecuteAsync(new CreatePermissionCommand(ownerId, "Orders.View", null));
            var approve = await scope.ServiceProvider.GetRequiredService<CreatePermissionUseCase>()
                .ExecuteAsync(new CreatePermissionCommand(ownerId, "Orders.Approve", null));
            Assert.True(view.IsSuccess && approve.IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<AssignPermissionToRoleUseCase>()
                .ExecuteAsync(new AssignPermissionToRoleCommand(ownerId, clerkRoleId, view.Value.Id))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<AssignPermissionToRoleUseCase>()
                .ExecuteAsync(new AssignPermissionToRoleCommand(ownerId, clerkRoleId, approve.Value.Id))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<SetUserPermissionOverrideUseCase>()
                .ExecuteAsync(new SetUserPermissionOverrideCommand(
                    ownerId, clerkUserId, approve.Value.Id, PermissionEffect.Deny))).IsSuccess);
        }

        return new DirectoryContext(
            ownerId,
            adminUserId,
            peerAdminUserId,
            clerkUserId,
            noRoleUserId,
            disabledUserId,
            lockedUserId,
            clerkLevel);
    }

    private static async Task<Guid> SeedRoleAsync(IServiceProvider sp, string name, int level)
    {
        var roles = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var role = new ApplicationRole(name, level);
        Assert.Equal(IdentityResult.Success, await roles.CreateAsync(role));
        return role.Id;
    }

    private static async Task<ApplicationUser> CreateNamedUserAsync(
        UserManager<ApplicationUser> users,
        string userName,
        string email)
    {
        var user = new ApplicationUser(userName) { Email = email };
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(user, "Passw0rd!"));
        return user;
    }

    private sealed record DirectoryContext(
        Guid OwnerId,
        Guid AdminUserId,
        Guid PeerAdminUserId,
        Guid ClerkUserId,
        Guid NoRoleUserId,
        Guid DisabledUserId,
        Guid LockedUserId,
        int ClerkLevel);

    private sealed record LoginTokens(string AccessToken, string RefreshToken);

    private sealed class ThrowAfterRevokeRefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly IRefreshTokenRepository _inner;

        public ThrowAfterRevokeRefreshTokenRepository(IRefreshTokenRepository inner)
        {
            _inner = inner;
        }

        public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            _inner.GetByTokenHashAsync(tokenHash, cancellationToken);

        public Task<RefreshToken?> GetByIdAsync(Guid refreshTokenId, CancellationToken cancellationToken = default) =>
            _inner.GetByIdAsync(refreshTokenId, cancellationToken);

        public Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyIdAsync(
            Guid familyId,
            CancellationToken cancellationToken = default) =>
            _inner.GetActiveByFamilyIdAsync(familyId, cancellationToken);

        public Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default) =>
            _inner.AddAsync(refreshToken, cancellationToken);

        public async Task<int> RevokeAllForUserAsync(
            Guid userId,
            DateTime revokedAtUtc,
            CancellationToken cancellationToken = default)
        {
            await _inner.RevokeAllForUserAsync(userId, revokedAtUtc, cancellationToken);
            throw new InvalidOperationException("Forced failure after refresh-token revocation.");
        }

        public Task<bool> FamilyExistsForUserAsync(
            Guid userId,
            Guid familyId,
            CancellationToken cancellationToken = default) =>
            _inner.FamilyExistsForUserAsync(userId, familyId, cancellationToken);

        public Task<int> RevokeFamilyForUserAsync(
            Guid userId,
            Guid familyId,
            DateTime revokedAtUtc,
            CancellationToken cancellationToken = default) =>
            _inner.RevokeFamilyForUserAsync(userId, familyId, revokedAtUtc, cancellationToken);

        public Task<bool> HasActiveFamilyForUserAsync(
            Guid userId,
            Guid familyId,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            _inner.HasActiveFamilyForUserAsync(userId, familyId, utcNow, cancellationToken);

        public Task<int> RevokeAllForUserExceptFamilyAsync(
            Guid userId,
            Guid currentFamilyId,
            DateTime revokedAtUtc,
            CancellationToken cancellationToken = default) =>
            _inner.RevokeAllForUserExceptFamilyAsync(userId, currentFamilyId, revokedAtUtc, cancellationToken);
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class UserDirectoryQueryTests : InfrastructureTestBase
{
    public UserDirectoryQueryTests(SqlServerContainerFixture sqlServer)
        : base(sqlServer)
    {
    }

    [Fact]
    public async Task SearchManageableUsers_QueryCount_DoesNotGrowWithDirectorySize()
    {
        using var scope = Fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var reader = scope.ServiceProvider.GetRequiredService<IIdentityUserReader>();

        var adminRole = new ApplicationRole("Admin", 10);
        var clerkRole = new ApplicationRole("Clerk", 20);
        Assert.Equal(IdentityResult.Success, await roles.CreateAsync(adminRole));
        Assert.Equal(IdentityResult.Success, await roles.CreateAsync(clerkRole));

        var actor = new ApplicationUser("dir.admin") { Email = "dir.admin@test.local" };
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(actor, "Passw0rd!"));
        Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(actor, "Admin"));

        await CreateClerksAsync(users, 8);
        var query = new IdentityUserSearchQuery(
            actor.Id, 10, DateTime.UtcNow, null, null, null, 1, 20);

        await reader.SearchManageableUsersAsync(query);
        Fixture.Interceptor.Reset();
        var first = await reader.SearchManageableUsersAsync(query);
        var firstCount = Fixture.Interceptor.Count;
        Assert.True(first.TotalCount >= 8);
        Assert.InRange(firstCount, 1, 6);

        await CreateClerksAsync(users, 12, start: 8);
        Fixture.Interceptor.Reset();
        var second = await reader.SearchManageableUsersAsync(query);
        Assert.True(second.TotalCount >= 20);
        Assert.Equal(firstCount, Fixture.Interceptor.Count);
    }

    [Fact]
    public async Task RevokeAllForUser_IsSingleSetBasedUpdate()
    {
        using var scope = Fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = new ApplicationUser("revoke.all") { Email = "revoke.all@test.local" };
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(user, "Passw0rd!"));

        for (var i = 0; i < 6; i++)
        {
            await tokens.AddAsync(
                RefreshToken.Create(
                    user.Id,
                    $"hash-{i}-{Guid.NewGuid():N}",
                    DateTime.UtcNow.AddDays(1),
                    Guid.NewGuid(),
                    createdAtUtc: DateTime.UtcNow),
                CancellationToken.None);
        }

        await db.SaveChangesAsync();
        Fixture.Interceptor.Reset();
        var revoked = await tokens.RevokeAllForUserAsync(user.Id, DateTime.UtcNow);
        Assert.Equal(6, revoked);
        Assert.Equal(1, Fixture.Interceptor.Count);
        Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.UserId == user.Id && t.RevokedAtUtc == null));
    }

    [Fact]
    public async Task RevokeAllForUserExceptFamily_IsSingleSetBasedUpdate()
    {
        using var scope = Fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = new ApplicationUser("revoke.except") { Email = "revoke.except@test.local" };
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(user, "Passw0rd!"));
        var keep = Guid.NewGuid();
        await tokens.AddAsync(
            RefreshToken.Create(user.Id, $"keep-{Guid.NewGuid():N}", DateTime.UtcNow.AddDays(1), keep, createdAtUtc: DateTime.UtcNow),
            CancellationToken.None);
        for (var i = 0; i < 5; i++)
        {
            await tokens.AddAsync(
                RefreshToken.Create(
                    user.Id,
                    $"other-{i}-{Guid.NewGuid():N}",
                    DateTime.UtcNow.AddDays(1),
                    Guid.NewGuid(),
                    createdAtUtc: DateTime.UtcNow),
                CancellationToken.None);
        }

        await db.SaveChangesAsync();
        Fixture.Interceptor.Reset();
        var revoked = await tokens.RevokeAllForUserExceptFamilyAsync(user.Id, keep, DateTime.UtcNow);
        Assert.Equal(5, revoked);
        Assert.Equal(1, Fixture.Interceptor.Count);
        Assert.Equal(1, await db.RefreshTokens.CountAsync(t => t.UserId == user.Id && t.RevokedAtUtc == null));
        Assert.True(await db.RefreshTokens.AnyAsync(t => t.FamilyId == keep && t.RevokedAtUtc == null));
    }

    [Fact]
    public async Task GetIamUserById_UsesBoundedJoin_NotPerRoleQueries()
    {
        using var scope = Fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var reader = scope.ServiceProvider.GetRequiredService<IIdentityUserReader>();

        var user = new ApplicationUser("many.roles") { Email = "many.roles@test.local" };
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(user, "Passw0rd!"));
        for (var i = 0; i < 6; i++)
        {
            var role = new ApplicationRole($"Role{i}", 30 + i);
            Assert.Equal(IdentityResult.Success, await roles.CreateAsync(role));
            Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(user, role.Name!));
        }

        Fixture.Interceptor.Reset();
        var record = await reader.GetIamUserByIdAsync(user.Id, DateTime.UtcNow);
        Assert.NotNull(record);
        Assert.Equal(30, record!.EffectiveRoleLevel);
        Assert.InRange(Fixture.Interceptor.Count, 1, 3);
    }

    private static async Task CreateClerksAsync(UserManager<ApplicationUser> users, int count, int start = 0)
    {
        for (var i = 0; i < count; i++)
        {
            var n = start + i;
            var clerk = new ApplicationUser($"dir.clerk{n:D2}") { Email = $"dir.clerk{n:D2}@test.local" };
            Assert.Equal(IdentityResult.Success, await users.CreateAsync(clerk, "Passw0rd!"));
            Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(clerk, "Clerk"));
        }
    }
}
