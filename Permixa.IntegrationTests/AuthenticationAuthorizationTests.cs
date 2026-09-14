using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Refresh;
using Permixa.Application.Authorization;
using Permixa.AspNetCore.Authentication;
using Permixa.Domain.Authorization;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Persistence;
using Permixa.IntegrationTests.Support;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Permixa.IntegrationTests;

public sealed class AuthenticationAuthorizationTests : IntegrationTestBase
{
    public AuthenticationAuthorizationTests(SqlServerContainerFixture sql, RedisContainerFixture redis)
        : base(sql, redis)
    {
    }

    [SkippableFact]
    public async Task IssuedAccessToken_ValidatesOnProtectedEndpoint()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        var (ownerId, token, userId, _, _) = await SeedOrdersPermissionAsync(host);

        var response = await host.AuthenticatedClient(token).GetAsync("/secure/orders-read");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(Guid.Empty, ownerId);
        Assert.NotEqual(Guid.Empty, userId);
    }

    [SkippableTheory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("wrong-signature")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("missing-sub")]
    [InlineData("malformed-sub")]
    public async Task JwtSecurityMatrix_Returns401(string scenario)
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.BootstrapAsync();

        var client = host.Client;
        if (scenario != "missing")
        {
            var token = scenario switch
            {
                "malformed" => "not-a-jwt",
                "expired" => TestKeys.CreateAccessToken(Guid.NewGuid(), TimeSpan.FromMinutes(-5)),
                "wrong-signature" => CreateForeignSignedToken(),
                "wrong-issuer" => TestKeys.CreateAccessToken(Guid.NewGuid(), issuer: "other-issuer"),
                "wrong-audience" => TestKeys.CreateAccessToken(Guid.NewGuid(), audience: "other-aud"),
                "missing-sub" => TestKeys.CreateAccessToken(null, includeSub: false),
                "malformed-sub" => TestKeys.CreateAccessToken(null, rawSub: "not-a-guid"),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            client = host.AuthenticatedClient(token);
        }

        var response = await client.GetAsync("/secure/orders-read");
        var problem = await ProblemJson.ReadAndAssertAsync(
            response,
            401,
            PermixaJwtBearerServiceCollectionExtensions.AuthenticationUnauthorizedCode);
        Assert.Contains(response.Headers.WwwAuthenticate, v => v.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("IDX", problem.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("signature", problem.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not-a-guid", problem.GetRawText(), StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task PermissionAbsent_Returns403_WithSafeProblemDetails()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        var user = await host.RegisterAsync("noperm", "noperm@permixa.test", TestKeys.UserPassword);
        var login = await host.LoginAsync(user.Email, TestKeys.UserPassword);

        var response = await host.AuthenticatedClient(login.AccessToken).GetAsync("/secure/orders-read");
        var problem = await ProblemJson.ReadAndAssertAsync(
            response,
            403,
            PermixaJwtBearerServiceCollectionExtensions.AuthorizationForbiddenCode);
        Assert.DoesNotContain("Orders.Read", problem.GetRawText(), StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task UserDenyOverride_WinsOverRoleGrant()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        var (ownerId, token, userId, permissionId, _) = await SeedOrdersPermissionAsync(host);

        var allowed = await host.AuthenticatedClient(token).GetAsync("/secure/orders-read");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        await host.SetOverrideAsync(ownerId, userId, permissionId, PermissionEffect.Deny);

        var denied = await host.AuthenticatedClient(token).GetAsync("/secure/orders-read");
        await ProblemJson.ReadAndAssertAsync(
            denied,
            403,
            PermixaJwtBearerServiceCollectionExtensions.AuthorizationForbiddenCode);
    }

    [SkippableFact]
    public async Task Redis_WritesSnapshot_ThenReusesIt()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        var (_, token, userId, _, _) = await SeedOrdersPermissionAsync(host);

        Assert.Equal(HttpStatusCode.OK, (await host.AuthenticatedClient(token).GetAsync("/secure/orders-read")).StatusCode);
        var setsAfterFirstProtectedCall = host.Cache!.SetCount;
        var getsAfterFirstProtectedCall = host.Cache.GetCount;
        Assert.True(setsAfterFirstProtectedCall >= 1);

        var mux = host.Services.GetRequiredService<IConnectionMultiplexer>();
        var cached = await mux.GetDatabase().StringGetAsync($"{host.RedisKeyPrefix}user:{userId:D}");
        Assert.False(cached.IsNullOrEmpty);
        var snapshot = cached.ToString();
        Assert.Contains("orders.read", snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain(token, snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain(TestKeys.UserPassword, snapshot, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await host.AuthenticatedClient(token).GetAsync("/secure/orders-read")).StatusCode);
        Assert.Equal(setsAfterFirstProtectedCall, host.Cache.SetCount);
        Assert.Equal(getsAfterFirstProtectedCall + 1, host.Cache.GetCount);
    }

    [SkippableFact]
    public async Task UserVersionChange_RejectsStaleRedisSnapshot()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        var (ownerId, token, userId, permissionId, _) = await SeedOrdersPermissionAsync(host);
        Assert.Equal(HttpStatusCode.OK, (await host.AuthenticatedClient(token).GetAsync("/secure/orders-read")).StatusCode);

        await host.SetOverrideAsync(ownerId, userId, permissionId, PermissionEffect.Deny);

        var denied = await host.AuthenticatedClient(token).GetAsync("/secure/orders-read");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [SkippableFact]
    public async Task RbacVersionChange_RebuildsAuthorization()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        var (ownerId, token, _, permissionId, roleId) = await SeedOrdersPermissionAsync(host);
        Assert.Equal(HttpStatusCode.OK, (await host.AuthenticatedClient(token).GetAsync("/secure/orders-read")).StatusCode);

        await host.RemovePermissionFromRoleAsync(ownerId, roleId, permissionId);

        var denied = await host.AuthenticatedClient(token).GetAsync("/secure/orders-read");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [SkippableFact]
    public async Task RedisOutage_FallsBackToSqlAuthorization()
    {
        RequireContainers();
        await using var isolatedRedis = new Testcontainers.Redis.RedisBuilder().Build();
        await isolatedRedis.StartAsync();
        try
        {
            await using var host = await StartHostAsync(o => o.RedisConnectionString = isolatedRedis.GetConnectionString());
            var (_, token, _, _, _) = await SeedOrdersPermissionAsync(host);
            Assert.Equal(HttpStatusCode.OK, (await host.AuthenticatedClient(token).GetAsync("/secure/orders-read")).StatusCode);

            await isolatedRedis.StopAsync();

            var afterOutage = await host.AuthenticatedClient(token).GetAsync("/secure/orders-read");
            Assert.Equal(HttpStatusCode.OK, afterOutage.StatusCode);
        }
        finally
        {
            await isolatedRedis.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task RefreshRotation_Reuse_Concurrent_AndLogout()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.RegisterAsync("session", "session@permixa.test", TestKeys.UserPassword);
        var first = await host.LoginAsync("session@permixa.test", TestKeys.UserPassword);
        var secondFamily = await host.LoginAsync("session@permixa.test", TestKeys.UserPassword);

        var rotated = await host.Client.PostAsJsonAsync("/refresh", new RefreshTokenRequest
        {
            RefreshToken = first.RefreshToken
        });
        rotated.EnsureSuccessStatusCode();
        var replacement = (await rotated.Content.ReadFromJsonAsync<AuthenticationResult>())!;
        Assert.NotEqual(first.RefreshToken, replacement.RefreshToken);

        await host.ExecuteScopedAsync(async sp =>
        {
            var crypto = sp.GetRequiredService<IRefreshTokenCrypto>();
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var oldHash = crypto.HashToken(first.RefreshToken);
            var newHash = crypto.HashToken(replacement.RefreshToken);
            var old = await db.RefreshTokens.SingleAsync(t => t.TokenHash == oldHash);
            var next = await db.RefreshTokens.SingleAsync(t => t.TokenHash == newHash);
            Assert.Equal(old.FamilyId, next.FamilyId);
            Assert.True(old.WasReplaced);
        });

        var protectedOk = await host.AuthenticatedClient(replacement.AccessToken).GetAsync("/me");
        Assert.Equal(HttpStatusCode.OK, protectedOk.StatusCode);

        var reuse = await host.Client.PostAsJsonAsync("/refresh", new RefreshTokenRequest
        {
            RefreshToken = first.RefreshToken
        });
        await ProblemJson.ReadAndAssertAsync(reuse, 401, AuthenticationErrors.RefreshTokenReuseDetected.Code);

        var afterReuse = await host.Client.PostAsJsonAsync("/refresh", new RefreshTokenRequest
        {
            RefreshToken = replacement.RefreshToken
        });
        await ProblemJson.ReadAndAssertAsync(afterReuse, 401, AuthenticationErrors.RefreshTokenRevoked.Code);

        var otherFamily = await host.Client.PostAsJsonAsync("/refresh", new RefreshTokenRequest
        {
            RefreshToken = secondFamily.RefreshToken
        });
        otherFamily.EnsureSuccessStatusCode();

        var logoutLogin = await host.LoginAsync("session@permixa.test", TestKeys.UserPassword);
        var logout = await host.Client.PostAsJsonAsync("/logout", new RevokeRefreshTokenRequest
        {
            RefreshToken = logoutLogin.RefreshToken
        });
        logout.EnsureSuccessStatusCode();
        var afterLogout = await host.Client.PostAsJsonAsync("/refresh", new RefreshTokenRequest
        {
            RefreshToken = logoutLogin.RefreshToken
        });
        await ProblemJson.ReadAndAssertAsync(afterLogout, 401, AuthenticationErrors.RefreshTokenRevoked.Code);

        var stillValidAccess = await host.AuthenticatedClient(logoutLogin.AccessToken).GetAsync("/me");
        Assert.Equal(HttpStatusCode.OK, stillValidAccess.StatusCode);
    }

    [SkippableFact]
    public async Task ConcurrentRefresh_OnlyOneSucceeds_WithoutFamilyTheft()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.RegisterAsync("concurrent", "concurrent@permixa.test", TestKeys.UserPassword);
        var login = await host.LoginAsync("concurrent@permixa.test", TestKeys.UserPassword);

        await using var scope1 = host.Services.CreateAsyncScope();
        await using var scope2 = host.Services.CreateAsyncScope();
        var refresh1 = scope1.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();
        var refresh2 = scope2.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();
        var request = new RefreshTokenRequest { RefreshToken = login.RefreshToken };

        var t1 = Task.Run(() => refresh1.ExecuteAsync(request));
        var t2 = Task.Run(() => refresh2.ExecuteAsync(request));
        var results = await Task.WhenAll(t1, t2);

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        var loser = results.Single(r => r.IsFailure);
        Assert.Equal(AuthenticationErrors.InvalidRefreshToken.Code, loser.Error!.Code);

        var winner = results.Single(r => r.IsSuccess);
        var winnerStillValid = await host.Client.PostAsJsonAsync("/refresh", new RefreshTokenRequest
        {
            RefreshToken = winner.Value.RefreshToken
        });
        winnerStillValid.EnsureSuccessStatusCode();
    }

    [SkippableFact]
    public async Task RoleLevel_HigherAuthorityMayAffectLower()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.BootstrapAsync();
        var owner = await host.LoginAsync(TestKeys.OwnerEmail, TestKeys.OwnerPassword);

        var manager = await host.RegisterUserInRoleAsync("manager", "Manager", roleLevel: 5);
        var staff = await host.RegisterUserInRoleAsync("staff", "Staff", roleLevel: 10);
        var ordersId = await host.CreatePermissionAsync("Orders.Assign");

        var ownerRoleId = await host.ExecuteScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            return (await db.Roles.SingleAsync(r => r.Name == SystemRoles.Owner)).Id;
        });
        var managePermissionId = await host.ExecuteScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            return (await db.Permissions.SingleAsync(p => p.Name == IamPermissions.RolePermissions.Manage)).Id;
        });

        await host.AssignPermissionToRoleAsync(owner.UserId, manager.RoleId, managePermissionId);

        var ownerOnStaff = await host.AssignPermissionToRoleResultAsync(owner.UserId, staff.RoleId, ordersId);
        Assert.True(ownerOnStaff.IsSuccess, ownerOnStaff.Error?.Description);

        var managerOnOwner = await host.AssignPermissionToRoleResultAsync(manager.UserId, ownerRoleId, ordersId);
        Assert.True(managerOnOwner.IsFailure);
        Assert.Equal(AuthorizationErrors.HierarchyViolation.Code, managerOnOwner.Error!.Code);

        var managerOnSelfRole = await host.AssignPermissionToRoleResultAsync(manager.UserId, manager.RoleId, ordersId);
        Assert.True(managerOnSelfRole.IsFailure);
        Assert.Equal(AuthorizationErrors.HierarchyViolation.Code, managerOnSelfRole.Error!.Code);
    }

    [SkippableFact]
    public async Task IssuedJwt_ContainsOnlyIdentityClaims()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.RegisterAsync("claims", "claims@permixa.test", TestKeys.UserPassword);
        var login = await host.LoginAsync("claims@permixa.test", TestKeys.UserPassword);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        var types = jwt.Claims.Select(c => c.Type).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(JwtRegisteredClaimNames.Sub, types);
        Assert.Contains(JwtRegisteredClaimNames.Jti, types);
        Assert.Contains(JwtRegisteredClaimNames.Iat, types);
        Assert.Contains(JwtRegisteredClaimNames.Exp, types);
        Assert.Contains(JwtRegisteredClaimNames.Iss, types);
        Assert.Contains(JwtRegisteredClaimNames.Aud, types);

        foreach (var banned in new[]
                 {
                     "permissions", "role", "roles", "RoleLevel", "email", "username",
                     "AuthorizationVersion", "SecurityStamp", ClaimTypesRole(), "rolelevel"
                 })
        {
            Assert.DoesNotContain(types, t => t.Equals(banned, StringComparison.OrdinalIgnoreCase));
        }

        Assert.True(types.IsSubsetOf(new[]
        {
            JwtRegisteredClaimNames.Sub,
            JwtRegisteredClaimNames.Jti,
            JwtRegisteredClaimNames.Iat,
            JwtRegisteredClaimNames.Exp,
            JwtRegisteredClaimNames.Iss,
            JwtRegisteredClaimNames.Aud,
            JwtRegisteredClaimNames.Nbf
        }));
    }

    private async Task<(Guid OwnerId, string AccessToken, Guid UserId, Guid PermissionId, Guid RoleId)> SeedOrdersPermissionAsync(
        PermixaTestHost host)
    {
        await host.BootstrapAsync();
        var owner = await host.LoginAsync(TestKeys.OwnerEmail, TestKeys.OwnerPassword);
        var seeded = await host.RegisterUserInRoleAsync("clerk", "Clerk", roleLevel: 50);
        var permissionId = await host.CreatePermissionAsync("Orders.Read");
        await host.AssignPermissionToRoleAsync(owner.UserId, seeded.RoleId, permissionId);
        var login = await host.LoginAsync(seeded.Email, TestKeys.UserPassword);
        return (owner.UserId, login.AccessToken, seeded.UserId, permissionId, seeded.RoleId);
    }

    private static string CreateForeignSignedToken()
    {
        using var other = RSA.Create(2048);
        return TestKeys.CreateAccessToken(Guid.NewGuid(), privatePem: other.ExportPkcs8PrivateKeyPem());
    }

    private static string ClaimTypesRole() => "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
}
