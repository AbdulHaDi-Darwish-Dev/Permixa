using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Verification.EmailConfirmation;
using Permixa.AspNetCore.Authentication;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Permixa.IntegrationTests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Permixa.IntegrationTests;

public sealed class HttpMappingAndAuditTests : IntegrationTestBase
{
    public HttpMappingAndAuditTests(SqlServerContainerFixture sql, RedisContainerFixture redis)
        : base(sql, redis)
    {
    }

    [SkippableTheory]
    [InlineData("validation", 400, "Test.Validation")]
    [InlineData("failure", 400, "Test.Failure")]
    [InlineData("notfound", 404, "Test.NotFound")]
    [InlineData("conflict", 409, "Test.Conflict")]
    [InlineData("unauthorized", 401, "Test.Unauthorized")]
    [InlineData("forbidden", 403, "Test.Forbidden")]
    public async Task ResultErrors_MapToProblemDetails(string kind, int status, string code)
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        var response = await host.Client.GetAsync($"/test/result/{kind}");
        var problem = await ProblemJson.ReadAndAssertAsync(response, status, code);
        Assert.Contains("detail", problem.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [SkippableTheory]
    [InlineData("concurrency", 409, "Concurrency.Conflict", "db conflict boom")]
    [InlineData("domain", 500, "InternalError", "domain boom")]
    [InlineData("email", 503, "Email.DeliveryFailed", "smtp boom")]
    [InlineData("other", 500, "InternalError", "unexpected boom")]
    public async Task UnexpectedExceptions_MapSafely(
        string kind,
        int status,
        string code,
        string rawMessage)
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        var response = await host.Client.GetAsync($"/test/exception/{kind}");
        var problem = await ProblemJson.ReadAndAssertAsync(response, status, code);
        var body = problem.GetRawText();
        Assert.DoesNotContain(rawMessage, body, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("DomainException", body, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task PersistenceAndLogs_DoNotStoreSecrets()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.BootstrapAsync();
        var user = await host.RegisterAsync("audit", "audit@permixa.test", TestKeys.UserPassword);
        var login = await host.LoginAsync("audit@permixa.test", TestKeys.UserPassword);
        await host.PostAsJsonSafe("/email-confirmation/request", user.UserId);

        var otp = host.Emails.ExtractOtp();
        var refresh = login.RefreshToken;
        var access = login.AccessToken;

        await host.ExecuteScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var identity = await users.FindByIdAsync(user.UserId.ToString());
            Assert.NotEqual(TestKeys.UserPassword, identity!.PasswordHash);

            var refreshHashes = await db.RefreshTokens.Select(t => t.TokenHash).ToListAsync();
            Assert.DoesNotContain(refresh, refreshHashes);
            Assert.All(refreshHashes, hash => Assert.DoesNotContain(refresh, hash, StringComparison.Ordinal));

            var blob = string.Join('\n',
                await db.RefreshTokens.Select(t => t.TokenHash).ToListAsync(),
                string.Join('\n', await db.VerificationChallenges
                    .Select(c => c.Destination + c.Purpose + c.Method + c.Channel)
                    .ToListAsync()),
                string.Join('\n', await db.Permissions.Select(p => p.Name).ToListAsync()));

            Assert.DoesNotContain(otp, blob, StringComparison.Ordinal);
            Assert.DoesNotContain(refresh, blob, StringComparison.Ordinal);
            Assert.DoesNotContain(access, blob, StringComparison.Ordinal);
            Assert.DoesNotContain(TestKeys.PrivateKeyPem, blob, StringComparison.Ordinal);
            Assert.DoesNotContain(TestKeys.FakeResendApiKey, blob, StringComparison.Ordinal);
            Assert.DoesNotContain(TestKeys.OwnerPassword, blob, StringComparison.Ordinal);
            Assert.DoesNotContain(TestKeys.UserPassword, blob, StringComparison.Ordinal);
        });

        var mux = host.Services.GetRequiredService<IConnectionMultiplexer>();
        var cached = await mux.GetDatabase().StringGetAsync($"{host.RedisKeyPrefix}user:{user.UserId:D}");
        if (!cached.IsNullOrEmpty)
        {
            var value = cached.ToString();
            Assert.DoesNotContain(refresh, value, StringComparison.Ordinal);
            Assert.DoesNotContain(access, value, StringComparison.Ordinal);
            Assert.DoesNotContain(otp, value, StringComparison.Ordinal);
            Assert.DoesNotContain(TestKeys.UserPassword, value, StringComparison.Ordinal);
        }

        var logs = string.Join('\n', host.Logs.Messages);
        foreach (var secret in new[]
                 {
                     TestKeys.UserPassword,
                     TestKeys.OwnerPassword,
                     refresh,
                     otp,
                     access,
                     TestKeys.PrivateKeyPem,
                     TestKeys.FakeResendApiKey
                 })
        {
            Assert.DoesNotContain(secret, logs, StringComparison.Ordinal);
        }
    }

    [SkippableFact]
    public async Task AnonymousAndAuthenticated_CurrentUserBoundary()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.RegisterAsync("meuser", "meuser@permixa.test", TestKeys.UserPassword);
        var login = await host.LoginAsync("meuser@permixa.test", TestKeys.UserPassword);

        var anonymous = await host.Client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Contains(anonymous.Headers.WwwAuthenticate, v => v.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase));

        var me = await host.AuthenticatedClient(login.AccessToken).GetAsync("/me");
        me.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal(login.UserId, doc.RootElement.GetProperty("userId").GetGuid());
    }
}

internal static class AuditHttpExtensions
{
    public static Task<HttpResponseMessage> PostAsJsonSafe(
        this PermixaTestHost host,
        string url,
        Guid userId) =>
        host.Client.PostAsJsonAsync(url, new RequestEmailConfirmationHttpRequest
        {
            UserId = userId,
            Method = "Otp"
        });
}
