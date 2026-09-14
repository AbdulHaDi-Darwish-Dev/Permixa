using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.Login;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Refresh;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.RolePermissions.Assign;
using Permixa.Application.Authorization.Sessions;
using Permixa.Application.Authorization.Sessions.Get;
using Permixa.Application.Authorization.Sessions.Revoke;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authentication;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;

namespace Permixa.Infrastructure.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class SessionAdministrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServer;
    private string _connectionString = null!;

    public SessionAdministrationTests(SqlServerContainerFixture sqlServer)
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
    public async Task Bootstrap_SeedsSessionPermissions_ForOwner_Idempotently()
    {
        await using var sp = BuildProvider();
        await MigrateBootstrapAsync(sp);
        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
        }

        using var assert = sp.CreateScope();
        var db = assert.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var owner = await assert.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync("owner@permixa.test");
        var snapshot = await assert.ServiceProvider.GetRequiredService<Permixa.Application.Authorization.Abstractions.IEffectivePermissionService>()
            .GetAuthorizationSnapshotAsync(owner!.Id);

        Assert.Equal(1, await db.Permissions.CountAsync(p => p.Name == IamPermissions.Sessions.Read));
        Assert.Equal(1, await db.Permissions.CountAsync(p => p.Name == IamPermissions.Sessions.Revoke));
        Assert.Equal(IamPermissions.All.Count, await db.Permissions.CountAsync());
        Assert.Equal(2, (await db.AuthorizationStates.SingleAsync()).RbacVersion);
        Assert.True(snapshot.Value.HasPermission(IamPermissions.Sessions.Read));
        Assert.True(snapshot.Value.HasPermission(IamPermissions.Sessions.Revoke));
    }

    [Fact]
    public async Task SessionProjection_AggregatesFamily_AndHidesHashes()
    {
        await using var sp = BuildProvider();
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);
        var user = await CreateUserAsync(sp, "sess.proj", "sess.proj@permixa.test");
        var first = await LoginAsync(sp, "sess.proj@permixa.test", "Passw0rd!");
        var second = await LoginAsync(sp, "sess.proj@permixa.test", "Passw0rd!");
        var rotated = await RefreshAsync(sp, first.RefreshToken);

        Guid familyA;
        Guid familyB;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var crypto = scope.ServiceProvider.GetRequiredService<IRefreshTokenCrypto>();
            familyA = (await db.RefreshTokens.SingleAsync(t =>
                t.TokenHash == crypto.HashToken(rotated.RefreshToken))).FamilyId;
            familyB = (await db.RefreshTokens.SingleAsync(t =>
                t.TokenHash == crypto.HashToken(second.RefreshToken))).FamilyId;
            Assert.NotEqual(familyA, familyB);
            Assert.Equal(2, await db.RefreshTokens.CountAsync(t => t.FamilyId == familyA));
        }

        using (var scope = sp.CreateScope())
        {
            var mine = await scope.ServiceProvider.GetRequiredService<GetMySessionsUseCase>()
                .ExecuteAsync(new GetMySessionsQuery(user, familyA));
            Assert.True(mine.IsSuccess, mine.Error?.Description);
            Assert.Equal(2, mine.Value.Count);
            Assert.Equal(familyB, mine.Value[0].FamilyId);
            Assert.Equal(familyA, mine.Value[1].FamilyId);
            Assert.True(mine.Value.Single(s => s.FamilyId == familyA).IsCurrent);
            Assert.False(mine.Value.Single(s => s.FamilyId == familyB).IsCurrent);
            Assert.All(mine.Value, s => Assert.True(s.ExpiresAtUtc > DateTime.UtcNow.AddHours(-1)));
        }

        await RevokeFamilyDirectAsync(sp, user, familyB);

        using (var scope = sp.CreateScope())
        {
            var mine = await scope.ServiceProvider.GetRequiredService<GetMySessionsUseCase>()
                .ExecuteAsync(new GetMySessionsQuery(user, familyA));
            Assert.Single(mine.Value);
            Assert.Equal(familyA, mine.Value[0].FamilyId);
        }

        _ = ownerId;
    }

    [Fact]
    public async Task ExpiredFamily_IsNotListed()
    {
        await using var sp = BuildProvider();
        var user = await CreateUserAfterBootstrapAsync(sp, "sess.exp", "sess.exp@permixa.test");
        await LoginAsync(sp, "sess.exp@permixa.test", "Passw0rd!");

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.RefreshTokens.ExecuteUpdateAsync(u =>
                u.SetProperty(t => t.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-5)));
        }

        using var assert = sp.CreateScope();
        var mine = await assert.ServiceProvider.GetRequiredService<GetMySessionsUseCase>()
            .ExecuteAsync(new GetMySessionsQuery(user));
        Assert.True(mine.IsSuccess);
        Assert.Empty(mine.Value);
    }

    [Fact]
    public async Task GetMySessions_CannotSeeAnotherUser()
    {
        await using var sp = BuildProvider();
        var a = await CreateUserAfterBootstrapAsync(sp, "sess.a", "sess.a@permixa.test");
        var b = await CreateUserAsync(sp, "sess.b", "sess.b@permixa.test");
        await LoginAsync(sp, "sess.a@permixa.test", "Passw0rd!");
        await LoginAsync(sp, "sess.b@permixa.test", "Passw0rd!");

        using var scope = sp.CreateScope();
        var mine = await scope.ServiceProvider.GetRequiredService<GetMySessionsUseCase>()
            .ExecuteAsync(new GetMySessionsQuery(a));
        Assert.Single(mine.Value);
        var other = await scope.ServiceProvider.GetRequiredService<ISessionReader>()
            .GetActiveFamiliesForUserAsync(b, DateTime.UtcNow);
        Assert.NotEqual(mine.Value[0].FamilyId, other[0].FamilyId);
    }

    [Fact]
    public async Task GetUserSessions_SecurityMatrix()
    {
        await using var sp = BuildProvider();
        var ctx = await SeedActorsAsync(sp);
        await LoginAsync(sp, "clerk.sess@permixa.test", "Passw0rd!");

        using var scope = sp.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<GetUserSessionsUseCase>();

        var ok = await sut.ExecuteAsync(new GetUserSessionsQuery(ctx.AdminUserId, ctx.ClerkUserId));
        Assert.True(ok.IsSuccess, ok.Error?.Description);
        Assert.Single(ok.Value);

        Assert.Equal(
            AuthorizationErrors.MissingManagePermission,
            (await sut.ExecuteAsync(new GetUserSessionsQuery(ctx.ClerkUserId, ctx.NoRoleUserId))).Error);
        Assert.Equal(
            AuthorizationErrors.CannotManageSelf,
            (await sut.ExecuteAsync(new GetUserSessionsQuery(ctx.AdminUserId, ctx.AdminUserId))).Error);
        Assert.Equal(
            AuthorizationErrors.HierarchyViolation,
            (await sut.ExecuteAsync(new GetUserSessionsQuery(ctx.AdminUserId, ctx.PeerAdminUserId))).Error);
        Assert.Equal(
            AuthorizationErrors.OwnerProtected,
            (await sut.ExecuteAsync(new GetUserSessionsQuery(ctx.AdminUserId, ctx.OwnerId))).Error);
    }

    [Fact]
    public async Task RevokeMySession_RevokesEntireFamily_AndIgnoresForeignFamily()
    {
        await using var sp = BuildProvider();
        var user = await CreateUserAfterBootstrapAsync(sp, "sess.rev", "sess.rev@permixa.test");
        var other = await CreateUserAsync(sp, "sess.other", "sess.other@permixa.test");
        var first = await LoginAsync(sp, "sess.rev@permixa.test", "Passw0rd!");
        var otherLogin = await LoginAsync(sp, "sess.other@permixa.test", "Passw0rd!");
        var rotated = await RefreshAsync(sp, first.RefreshToken);

        Guid family;
        Guid otherFamily;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var crypto = scope.ServiceProvider.GetRequiredService<IRefreshTokenCrypto>();
            family = (await db.RefreshTokens.SingleAsync(t =>
                t.TokenHash == crypto.HashToken(rotated.RefreshToken))).FamilyId;
            otherFamily = (await db.RefreshTokens.SingleAsync(t =>
                t.TokenHash == crypto.HashToken(otherLogin.RefreshToken))).FamilyId;
        }

        using (var scope = sp.CreateScope())
        {
            var revoke = scope.ServiceProvider.GetRequiredService<RevokeMySessionUseCase>();
            Assert.True((await revoke.ExecuteAsync(new RevokeMySessionCommand(user, family))).IsSuccess);
            Assert.True((await revoke.ExecuteAsync(new RevokeMySessionCommand(user, family))).IsSuccess);
            Assert.Equal(
                AuthorizationErrors.SessionNotFound,
                (await revoke.ExecuteAsync(new RevokeMySessionCommand(user, otherFamily))).Error);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.FamilyId == family && t.RevokedAtUtc == null));
            Assert.Equal(1, await db.RefreshTokens.CountAsync(t => t.FamilyId == otherFamily && t.RevokedAtUtc == null));
            Assert.Equal(
                AuthenticationErrors.RefreshTokenRevoked.Code,
                (await scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>()
                    .ExecuteAsync(new RefreshTokenRequest { RefreshToken = rotated.RefreshToken })).Error!.Code);
        }

        _ = other;
    }

    [Fact]
    public async Task RevokeUserSession_AndRevokeAll_HonorHierarchy_AndVersions()
    {
        await using var sp = BuildProvider();
        var ctx = await SeedActorsAsync(sp);
        var clerkLogin = await LoginAsync(sp, "clerk.sess@permixa.test", "Passw0rd!");
        var noRole1 = await LoginAsync(sp, "norole.sess@permixa.test", "Passw0rd!");
        var noRole2 = await LoginAsync(sp, "norole.sess@permixa.test", "Passw0rd!");
        var adminLogin = await LoginAsync(sp, "admin.sess@permixa.test", "Passw0rd!");

        int rbacBefore;
        int clerkVersionBefore;
        Guid clerkFamily;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var crypto = scope.ServiceProvider.GetRequiredService<IRefreshTokenCrypto>();
            rbacBefore = (await db.AuthorizationStates.SingleAsync()).RbacVersion;
            clerkVersionBefore = (await db.Users.SingleAsync(u => u.Id == ctx.ClerkUserId)).AuthorizationVersion;
            clerkFamily = (await db.RefreshTokens.SingleAsync(t =>
                t.TokenHash == crypto.HashToken(clerkLogin.RefreshToken))).FamilyId;
        }

        using (var scope = sp.CreateScope())
        {
            var revokeOne = scope.ServiceProvider.GetRequiredService<RevokeUserSessionUseCase>();
            Assert.Equal(
                AuthorizationErrors.MissingManagePermission,
                (await revokeOne.ExecuteAsync(new RevokeUserSessionCommand(ctx.ClerkUserId, ctx.NoRoleUserId, clerkFamily))).Error);
            Assert.Equal(
                AuthorizationErrors.OwnerProtected,
                (await revokeOne.ExecuteAsync(new RevokeUserSessionCommand(ctx.AdminUserId, ctx.OwnerId, clerkFamily))).Error);
            Assert.Equal(
                AuthorizationErrors.HierarchyViolation,
                (await revokeOne.ExecuteAsync(new RevokeUserSessionCommand(ctx.AdminUserId, ctx.PeerAdminUserId, clerkFamily))).Error);
            Assert.Equal(
                AuthorizationErrors.SessionNotFound,
                (await revokeOne.ExecuteAsync(new RevokeUserSessionCommand(ctx.AdminUserId, ctx.ClerkUserId, Guid.NewGuid()))).Error);

            Assert.True((await revokeOne.ExecuteAsync(
                new RevokeUserSessionCommand(ctx.AdminUserId, ctx.ClerkUserId, clerkFamily))).IsSuccess);
        }

        using (var scope = sp.CreateScope())
        {
            var revokeAll = scope.ServiceProvider.GetRequiredService<RevokeAllUserSessionsUseCase>();
            Assert.True((await revokeAll.ExecuteAsync(
                new RevokeAllUserSessionsCommand(ctx.AdminUserId, ctx.NoRoleUserId))).IsSuccess);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.RefreshTokens.CountAsync(t =>
                t.UserId == ctx.NoRoleUserId && t.RevokedAtUtc == null));
            Assert.Equal(1, await db.RefreshTokens.CountAsync(t =>
                t.UserId == ctx.AdminUserId && t.RevokedAtUtc == null));
            Assert.Equal(rbacBefore, (await db.AuthorizationStates.SingleAsync()).RbacVersion);
            Assert.Equal(clerkVersionBefore, (await db.Users.SingleAsync(u => u.Id == ctx.ClerkUserId)).AuthorizationVersion);

            var refresh = scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();
            Assert.Equal(
                AuthenticationErrors.RefreshTokenRevoked.Code,
                (await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = noRole1.RefreshToken })).Error!.Code);
            Assert.Equal(
                AuthenticationErrors.RefreshTokenRevoked.Code,
                (await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = noRole2.RefreshToken })).Error!.Code);
            Assert.True((await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = adminLogin.RefreshToken })).IsSuccess);
        }
    }

    [Fact]
    public async Task RevokeAllMySessions_IncludesCurrent_AndIsIdempotent()
    {
        await using var sp = BuildProvider();
        var user = await CreateUserAfterBootstrapAsync(sp, "sess.all", "sess.all@permixa.test");
        var a = await LoginAsync(sp, "sess.all@permixa.test", "Passw0rd!");
        var b = await LoginAsync(sp, "sess.all@permixa.test", "Passw0rd!");

        using (var scope = sp.CreateScope())
        {
            var sut = scope.ServiceProvider.GetRequiredService<RevokeAllMySessionsUseCase>();
            Assert.True((await sut.ExecuteAsync(new RevokeAllMySessionsCommand(user))).IsSuccess);
            Assert.True((await sut.ExecuteAsync(new RevokeAllMySessionsCommand(user))).IsSuccess);
        }

        using (var scope = sp.CreateScope())
        {
            var refresh = scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();
            Assert.True((await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = a.RefreshToken })).IsFailure);
            Assert.True((await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = b.RefreshToken })).IsFailure);
            Assert.Empty((await scope.ServiceProvider.GetRequiredService<GetMySessionsUseCase>()
                .ExecuteAsync(new GetMySessionsQuery(user))).Value);
        }
    }

    [Fact]
    public async Task RevokeSession_DoesNotBlacklistAccessJwt()
    {
        await using var sp = BuildProvider();
        var user = await CreateUserAfterBootstrapAsync(sp, "sess.jwt", "sess.jwt@permixa.test");
        var auth = await LoginAsync(sp, "sess.jwt@permixa.test", "Passw0rd!");

        Guid family;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var crypto = scope.ServiceProvider.GetRequiredService<IRefreshTokenCrypto>();
            family = (await db.RefreshTokens.SingleAsync(t =>
                t.TokenHash == crypto.HashToken(auth.RefreshToken))).FamilyId;
        }

        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<RevokeMySessionUseCase>()
                .ExecuteAsync(new RevokeMySessionCommand(user, family))).IsSuccess);
            Assert.Equal(
                AuthenticationErrors.RefreshTokenRevoked.Code,
                (await scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>()
                    .ExecuteAsync(new RefreshTokenRequest { RefreshToken = auth.RefreshToken })).Error!.Code);
        }

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken);
        Assert.Equal("RS256", jwt.Header.Alg);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);
    }

    [Fact]
    public async Task Refresh_Versus_RevokeFamily_DoesNotResurrectFamily()
    {
        await using var sp = BuildProvider();
        var user = await CreateUserAfterBootstrapAsync(sp, "sess.race", "sess.race@permixa.test");
        var auth = await LoginAsync(sp, "sess.race@permixa.test", "Passw0rd!");

        Guid family;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var crypto = scope.ServiceProvider.GetRequiredService<IRefreshTokenCrypto>();
            family = (await db.RefreshTokens.SingleAsync(t =>
                t.TokenHash == crypto.HashToken(auth.RefreshToken))).FamilyId;
        }

        await using var refreshScope = sp.CreateAsyncScope();
        await using var revokeScope = sp.CreateAsyncScope();
        var refreshTask = refreshScope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>()
            .ExecuteAsync(new RefreshTokenRequest { RefreshToken = auth.RefreshToken });
        var revokeTask = revokeScope.ServiceProvider.GetRequiredService<RevokeMySessionUseCase>()
            .ExecuteAsync(new RevokeMySessionCommand(user, family));
        await Task.WhenAll(refreshTask, revokeTask);
        var refreshResult = await refreshTask;
        await revokeTask;

        using var assert = sp.CreateScope();
        var dbAssert = assert.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await dbAssert.RefreshTokens.CountAsync(t =>
            t.FamilyId == family && t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow));

        if (refreshResult.IsSuccess)
        {
            var again = await assert.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>()
                .ExecuteAsync(new RefreshTokenRequest { RefreshToken = refreshResult.Value.RefreshToken });
            Assert.True(again.IsFailure, "A usable continuation must not survive family revocation.");
        }
    }

    [Fact]
    public async Task Refresh_Versus_RevokeAll_DoesNotLeaveActiveContinuation()
    {
        await using var sp = BuildProvider();
        var user = await CreateUserAfterBootstrapAsync(sp, "sess.raceall", "sess.raceall@permixa.test");
        var auth = await LoginAsync(sp, "sess.raceall@permixa.test", "Passw0rd!");

        await using var refreshScope = sp.CreateAsyncScope();
        await using var revokeScope = sp.CreateAsyncScope();
        var refreshTask = refreshScope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>()
            .ExecuteAsync(new RefreshTokenRequest { RefreshToken = auth.RefreshToken });
        var revokeTask = revokeScope.ServiceProvider.GetRequiredService<RevokeAllMySessionsUseCase>()
            .ExecuteAsync(new RevokeAllMySessionsCommand(user));
        await Task.WhenAll(refreshTask, revokeTask);
        var refreshResult = await refreshTask;
        await revokeTask;

        using var assert = sp.CreateScope();
        var db = assert.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.RefreshTokens.CountAsync(t =>
            t.UserId == user && t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow));

        if (refreshResult.IsSuccess)
        {
            var again = await assert.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>()
                .ExecuteAsync(new RefreshTokenRequest { RefreshToken = refreshResult.Value.RefreshToken });
            Assert.True(again.IsFailure, "A usable continuation must not survive revoke-all.");
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
        services.AddPermixaAuthentication(o =>
        {
            o.Jwt.Issuer = TestJwtKeys.Issuer;
            o.Jwt.Audience = TestJwtKeys.Audience;
            o.Jwt.PrivateKeyPem = TestJwtKeys.PrivateKeyPem;
            o.Jwt.AccessTokenLifetime = TimeSpan.FromMinutes(15);
            o.Authentication.RefreshTokenLifetime = TimeSpan.FromDays(7);
        });
        return services.BuildServiceProvider();
    }

    private static async Task MigrateBootstrapAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
    }

    private static async Task<Guid> MigrateBootstrapAndGetOwnerAsync(ServiceProvider sp)
    {
        await MigrateBootstrapAsync(sp);
        using var scope = sp.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync("owner@permixa.test"))!.Id;
    }

    private static async Task<Guid> CreateUserAfterBootstrapAsync(ServiceProvider sp, string userName, string email)
    {
        await MigrateBootstrapAsync(sp);
        return await CreateUserAsync(sp, userName, email);
    }

    private static async Task<Guid> CreateUserAsync(ServiceProvider sp, string userName, string email)
    {
        using var scope = sp.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser(userName) { Email = email };
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(user, "Passw0rd!"));
        return user.Id;
    }

    private static async Task<AuthenticationResult> LoginAsync(ServiceProvider sp, string email, string password)
    {
        using var scope = sp.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<LoginUseCase>()
            .ExecuteAsync(new LoginRequest { EmailOrUserName = email, Password = password });
        Assert.True(result.IsSuccess, result.Error?.Description);
        Assert.True(result.Value.IsAuthenticated);
        return result.Value.Authentication!;
    }

    private static async Task<AuthenticationResult> RefreshAsync(ServiceProvider sp, string refreshToken)
    {
        using var scope = sp.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>()
            .ExecuteAsync(new RefreshTokenRequest { RefreshToken = refreshToken });
        Assert.True(result.IsSuccess, result.Error?.Description);
        return result.Value;
    }

    private static async Task RevokeFamilyDirectAsync(ServiceProvider sp, Guid userId, Guid familyId)
    {
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>()
            .RevokeFamilyForUserAsync(userId, familyId, DateTime.UtcNow);
    }

    private static async Task<ActorContext> SeedActorsAsync(ServiceProvider sp)
    {
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);
        Guid adminUserId, peerAdminUserId, clerkUserId, noRoleUserId, adminRoleId;
        using (var scope = sp.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var adminRole = new ApplicationRole("Admin", 10);
            var peerRole = new ApplicationRole("PeerAdmin", 10);
            var clerkRole = new ApplicationRole("Clerk", 20);
            Assert.Equal(IdentityResult.Success, await roles.CreateAsync(adminRole));
            Assert.Equal(IdentityResult.Success, await roles.CreateAsync(peerRole));
            Assert.Equal(IdentityResult.Success, await roles.CreateAsync(clerkRole));
            adminRoleId = adminRole.Id;

            var admin = await CreateNamedAsync(users, "admin.sess", "admin.sess@permixa.test");
            var peer = await CreateNamedAsync(users, "peer.sess", "peer.sess@permixa.test");
            var clerk = await CreateNamedAsync(users, "clerk.sess", "clerk.sess@permixa.test");
            var noRole = await CreateNamedAsync(users, "norole.sess", "norole.sess@permixa.test");
            Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(admin, "Admin"));
            Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(peer, "PeerAdmin"));
            Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(clerk, "Clerk"));
            adminUserId = admin.Id;
            peerAdminUserId = peer.Id;
            clerkUserId = clerk.Id;
            noRoleUserId = noRole.Id;

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var name in new[] { IamPermissions.Sessions.Read, IamPermissions.Sessions.Revoke })
            {
                var permissionId = (await db.Permissions.SingleAsync(p => p.Name == name)).Id;
                Assert.True((await scope.ServiceProvider.GetRequiredService<AssignPermissionToRoleUseCase>()
                    .ExecuteAsync(new AssignPermissionToRoleCommand(ownerId, adminRoleId, permissionId))).IsSuccess);
            }
        }

        return new ActorContext(ownerId, adminUserId, peerAdminUserId, clerkUserId, noRoleUserId);
    }

    private static async Task<ApplicationUser> CreateNamedAsync(
        UserManager<ApplicationUser> users, string userName, string email)
    {
        var user = new ApplicationUser(userName) { Email = email };
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(user, "Passw0rd!"));
        return user;
    }

    private sealed record ActorContext(
        Guid OwnerId,
        Guid AdminUserId,
        Guid PeerAdminUserId,
        Guid ClerkUserId,
        Guid NoRoleUserId);
}

[Collection(SqlServerCollection.Name)]
public sealed class SessionQueryTests : InfrastructureTestBase
{
    public SessionQueryTests(SqlServerContainerFixture sqlServer)
        : base(sqlServer)
    {
    }

    [Fact]
    public async Task GetActiveFamilies_QueryCount_DoesNotGrowWithFamilyCount()
    {
        using var scope = Fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionReader>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = new ApplicationUser("sess.query") { Email = "sess.query@test.local" };
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(user, "Passw0rd!"));

        await AddFamiliesAsync(tokens, db, user.Id, 4);
        await sessions.GetActiveFamiliesForUserAsync(user.Id, DateTime.UtcNow);
        Fixture.Interceptor.Reset();
        var first = await sessions.GetActiveFamiliesForUserAsync(user.Id, DateTime.UtcNow);
        var firstCount = Fixture.Interceptor.Count;
        Assert.Equal(4, first.Count);
        Assert.InRange(firstCount, 1, 4);

        await AddFamiliesAsync(tokens, db, user.Id, 8);
        Fixture.Interceptor.Reset();
        var second = await sessions.GetActiveFamiliesForUserAsync(user.Id, DateTime.UtcNow);
        Assert.Equal(12, second.Count);
        Assert.Equal(firstCount, Fixture.Interceptor.Count);
    }

    private static async Task AddFamiliesAsync(
        IRefreshTokenRepository tokens,
        ApplicationDbContext db,
        Guid userId,
        int count)
    {
        for (var i = 0; i < count; i++)
        {
            await tokens.AddAsync(
                RefreshToken.Create(
                    userId,
                    $"sess-hash-{Guid.NewGuid():N}",
                    DateTime.UtcNow.AddDays(1),
                    Guid.NewGuid(),
                    createdAtUtc: DateTime.UtcNow.AddMinutes(-i)),
                CancellationToken.None);
        }

        await db.SaveChangesAsync();
    }
}
