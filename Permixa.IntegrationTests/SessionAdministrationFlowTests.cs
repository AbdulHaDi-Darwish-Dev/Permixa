using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.Logout;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Refresh;
using Permixa.Application.Authorization.Sessions.Get;
using Permixa.Application.Authorization.Sessions.Revoke;
using Permixa.Infrastructure.Persistence;
using Permixa.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.IntegrationTests;

public sealed class SessionAdministrationFlowTests : IntegrationTestBase
{
    public SessionAdministrationFlowTests(SqlServerContainerFixture sql, RedisContainerFixture redis)
        : base(sql, redis)
    {
    }

    [SkippableFact]
    public async Task LoginRevokeLogoutAndRevokeAll_FollowFamilySessionSemantics()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.BootstrapAsync();

        var registered = await host.RegisterAsync("sess.flow", "sess.flow@permixa.test", TestKeys.UserPassword);
        var loginA = await host.LoginAsync("sess.flow@permixa.test", TestKeys.UserPassword);
        var loginB = await host.LoginAsync("sess.flow@permixa.test", TestKeys.UserPassword);

        var sessions = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<GetMySessionsUseCase>().ExecuteAsync(
                new GetMySessionsQuery(registered.UserId)));
        Assert.True(sessions.IsSuccess, sessions.Error?.Description);
        Assert.Equal(2, sessions.Value.Count);

        var familyA = await host.ExecuteScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var crypto = sp.GetRequiredService<IRefreshTokenCrypto>();
            return (await db.RefreshTokens.SingleAsync(t =>
                t.TokenHash == crypto.HashToken(loginA.RefreshToken))).FamilyId;
        });

        var revokeA = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<RevokeMySessionUseCase>().ExecuteAsync(
                new RevokeMySessionCommand(registered.UserId, familyA)));
        Assert.True(revokeA.IsSuccess, revokeA.Error?.Description);

        await ProblemJson.ReadAndAssertAsync(
            await host.Client.PostAsJsonAsync("/refresh", new RefreshTokenRequest { RefreshToken = loginA.RefreshToken }),
            401,
            AuthenticationErrors.RefreshTokenRevoked.Code);

        var stillB = await host.Client.PostAsJsonAsync(
            "/refresh", new RefreshTokenRequest { RefreshToken = loginB.RefreshToken });
        stillB.EnsureSuccessStatusCode();
        var rotatedB = await stillB.Content.ReadFromJsonAsync<AuthenticationResult>();
        Assert.NotNull(rotatedB);

        await host.ExecuteScopedAsync(async sp =>
        {
            var logout = await sp.GetRequiredService<LogoutUseCase>()
                .ExecuteAsync(new RevokeRefreshTokenRequest { RefreshToken = rotatedB.RefreshToken });
            Assert.True(logout.IsSuccess, logout.Error?.Description);
        });

        await ProblemJson.ReadAndAssertAsync(
            await host.Client.PostAsJsonAsync("/refresh", new RefreshTokenRequest { RefreshToken = rotatedB.RefreshToken }),
            401,
            AuthenticationErrors.RefreshTokenRevoked.Code);

        var loginC = await host.LoginAsync("sess.flow@permixa.test", TestKeys.UserPassword);
        var loginD = await host.LoginAsync("sess.flow@permixa.test", TestKeys.UserPassword);

        var revokeAll = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<RevokeAllMySessionsUseCase>().ExecuteAsync(
                new RevokeAllMySessionsCommand(registered.UserId)));
        Assert.True(revokeAll.IsSuccess, revokeAll.Error?.Description);

        await ProblemJson.ReadAndAssertAsync(
            await host.Client.PostAsJsonAsync("/refresh", new RefreshTokenRequest { RefreshToken = loginC.RefreshToken }),
            401,
            AuthenticationErrors.RefreshTokenRevoked.Code);
        await ProblemJson.ReadAndAssertAsync(
            await host.Client.PostAsJsonAsync("/refresh", new RefreshTokenRequest { RefreshToken = loginD.RefreshToken }),
            401,
            AuthenticationErrors.RefreshTokenRevoked.Code);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(loginD.AccessToken);
        Assert.Equal("RS256", jwt.Header.Alg);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);
    }
}
