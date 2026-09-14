using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Permixa.AspNetCore.Authentication;
using Permixa.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Permixa.AspNetCore.Tests;

public sealed class RateLimitingOptionsValidationTests
{
    [Fact]
    public void AddFixedWindow_RejectsInvalidLimits()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddFixedWindow("A", p =>
            {
                p.PermitLimit = 0;
                p.Window = TimeSpan.FromMinutes(1);
            })));

        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddFixedWindow("A", p =>
            {
                p.PermitLimit = -1;
                p.Window = TimeSpan.FromMinutes(1);
            })));

        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddFixedWindow("A", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.Zero;
            })));

        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddFixedWindow("A", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.FromMinutes(-1);
            })));

        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddFixedWindow("A", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.FromMinutes(1);
                p.QueueLimit = -1;
            })));
    }

    [Fact]
    public void AddSlidingWindow_RejectsInvalidSegmentsAndLimits()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddSlidingWindow("A", p =>
            {
                p.PermitLimit = 0;
                p.Window = TimeSpan.FromMinutes(1);
                p.SegmentsPerWindow = 1;
            })));

        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddSlidingWindow("A", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.Zero;
                p.SegmentsPerWindow = 1;
            })));

        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddSlidingWindow("A", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.FromMinutes(1);
                p.SegmentsPerWindow = 0;
            })));

        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddSlidingWindow("A", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.FromMinutes(1);
                p.SegmentsPerWindow = -2;
            })));

        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddSlidingWindow("A", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.FromMinutes(1);
                p.SegmentsPerWindow = 2;
                p.QueueLimit = -1;
            })));
    }

    [Fact]
    public void AddTokenBucket_RejectsInvalidLimits()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddTokenBucket("A", p =>
            {
                p.TokenLimit = 0;
                p.TokensPerPeriod = 1;
                p.ReplenishmentPeriod = TimeSpan.FromSeconds(1);
            })));

        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddTokenBucket("A", p =>
            {
                p.TokenLimit = 1;
                p.TokensPerPeriod = 0;
                p.ReplenishmentPeriod = TimeSpan.FromSeconds(1);
            })));

        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddTokenBucket("A", p =>
            {
                p.TokenLimit = 1;
                p.TokensPerPeriod = 1;
                p.ReplenishmentPeriod = TimeSpan.Zero;
            })));

        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddTokenBucket("A", p =>
            {
                p.TokenLimit = 1;
                p.TokensPerPeriod = 1;
                p.ReplenishmentPeriod = TimeSpan.FromSeconds(-1);
            })));

        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddTokenBucket("A", p =>
            {
                p.TokenLimit = 1;
                p.TokensPerPeriod = 1;
                p.ReplenishmentPeriod = TimeSpan.FromSeconds(1);
                p.QueueLimit = -1;
            })));
    }

    [Fact]
    public void AddConcurrency_RejectsInvalidLimits()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddConcurrency("A", p => p.PermitLimit = 0)));

        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddConcurrency("A", p =>
            {
                p.PermitLimit = 1;
                p.QueueLimit = -1;
            })));
    }

    [Fact]
    public void DuplicatePolicyNames_AreRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Configure(o =>
            {
                o.AddFixedWindow("Login", p =>
                {
                    p.PermitLimit = 5;
                    p.Window = TimeSpan.FromMinutes(1);
                });
                o.AddTokenBucket("Login", p =>
                {
                    p.TokenLimit = 3;
                    p.TokensPerPeriod = 1;
                    p.ReplenishmentPeriod = TimeSpan.FromMinutes(1);
                });
            }));

        Assert.Contains("Login", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyPolicyName_IsRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            Configure(o => o.AddFixedWindow("  ", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.FromMinutes(1);
            })));
    }

    [Fact]
    public void AddPermixaRateLimiting_RequiresAtLeastOnePolicy()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPermixaRateLimiting(_ => { }));
        Assert.Contains("at least one policy", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PolicyName_IsPreservedWithoutPermixaPrefix()
    {
        var options = new PermixaRateLimitingOptions();
        options.AddFixedWindow("PublicApi", p =>
        {
            p.PermitLimit = 10;
            p.Window = TimeSpan.FromMinutes(1);
        });

        Assert.Contains("PublicApi", options.PolicyNames);
        Assert.DoesNotContain(options.PolicyNames, n => n.Contains("Permixa", StringComparison.Ordinal));
    }

    private static void Configure(Action<PermixaRateLimitingOptions> configure)
    {
        var options = new PermixaRateLimitingOptions();
        configure(options);
    }
}

public sealed class RateLimitingPartitionKeyTests
{
    [Fact]
    public void RemoteIp_UsesConnectionAddress_NotQueryEmail()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        context.Request.QueryString = new QueryString("?email=victim@example.com&username=victim");

        var key = PermixaRateLimitingOptions.ResolvePartitionKey(
            context,
            PermixaRateLimitPartitionKind.RemoteIp);

        Assert.Equal("203.0.113.10", key);
        Assert.DoesNotContain("victim", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", key, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthenticatedUserId_UsesStableUserClaim_NotTokenPayloadSecrets()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.2");
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim(
                    System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                    userId.ToString("D"))
            ],
            authenticationType: "Bearer"));

        var key = PermixaRateLimitingOptions.ResolvePartitionKey(
            context,
            PermixaRateLimitPartitionKind.AuthenticatedUserId);

        Assert.Equal("user:11111111-1111-1111-1111-111111111111", key);
        Assert.DoesNotContain("Bearer", key, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Global_UsesSingleSharedKey()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        Assert.Equal(
            "global",
            PermixaRateLimitingOptions.ResolvePartitionKey(
                context,
                PermixaRateLimitPartitionKind.Global));
    }
}

public sealed class RateLimitingIntegrationTests
{
    [Fact]
    public async Task FixedWindow_AllowsUpToLimit_ThenReturns429()
    {
        using var host = await CreateHostAsync(o =>
            o.AddFixedWindow("PublicApi", p =>
            {
                p.PermitLimit = 3;
                p.Window = TimeSpan.FromMinutes(5);
                p.QueueLimit = 0;
                p.Partition = PermixaRateLimitPartitionKind.Global;
            }),
            app => app.MapGet("/public", () => Results.Ok("ok")).RequireRateLimiting("PublicApi"));

        var client = host.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/public")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/public")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/public")).StatusCode);

        var rejected = await client.GetAsync("/public");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        await AssertGeneric429Async(rejected);
    }

    [Fact]
    public async Task FixedWindow_ResetsAfterWindow()
    {
        using var host = await CreateHostAsync(o =>
            o.AddFixedWindow("Short", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.FromMilliseconds(250);
                p.QueueLimit = 0;
                p.Partition = PermixaRateLimitPartitionKind.Global;
            }),
            app => app.MapGet("/short", () => Results.Ok("ok")).RequireRateLimiting("Short"));

        var client = host.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/short")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/short")).StatusCode);

        await Task.Delay(350);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/short")).StatusCode);
    }

    [Fact]
    public async Task SlidingWindow_EnforcesLimit()
    {
        using var host = await CreateHostAsync(o =>
            o.AddSlidingWindow("Login", p =>
            {
                p.PermitLimit = 2;
                p.Window = TimeSpan.FromMinutes(1);
                p.SegmentsPerWindow = 6;
                p.QueueLimit = 0;
                p.Partition = PermixaRateLimitPartitionKind.Global;
            }),
            app => app.MapGet("/login", () => Results.Ok("ok")).RequireRateLimiting("Login"));

        var client = host.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/login")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/login")).StatusCode);
    }

    [Fact]
    public async Task TokenBucket_ExhaustsThenReplenishes()
    {
        using var host = await CreateHostAsync(o =>
            o.AddTokenBucket("Otp", p =>
            {
                p.TokenLimit = 2;
                p.TokensPerPeriod = 1;
                p.ReplenishmentPeriod = TimeSpan.FromMilliseconds(200);
                p.QueueLimit = 0;
                p.AutoReplenishment = true;
                p.Partition = PermixaRateLimitPartitionKind.Global;
            }),
            app => app.MapGet("/otp", () => Results.Ok("ok")).RequireRateLimiting("Otp"));

        var client = host.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/otp")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/otp")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/otp")).StatusCode);

        await Task.Delay(350);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/otp")).StatusCode);
    }

    [Fact]
    public async Task Concurrency_RejectsWhenNoQueueAvailable()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var host = await CreateHostAsync(o =>
            o.AddConcurrency("Heavy", p =>
            {
                p.PermitLimit = 1;
                p.QueueLimit = 0;
                p.Partition = PermixaRateLimitPartitionKind.Global;
            }),
            app => app.MapGet("/heavy", async () =>
            {
                entered.TrySetResult();
                await release.Task;
                return Results.Ok("done");
            }).RequireRateLimiting("Heavy"));

        var client = host.GetTestClient();
        var first = client.GetAsync("/heavy");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = await client.GetAsync("/heavy");
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);

        release.TrySetResult();
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
    }

    [Fact]
    public async Task Concurrency_QueueAllowsWaitingRequest()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var host = await CreateHostAsync(o =>
            o.AddConcurrency("Queued", p =>
            {
                p.PermitLimit = 1;
                p.QueueLimit = 1;
                p.Partition = PermixaRateLimitPartitionKind.Global;
            }),
            app => app.MapGet("/queued", async () =>
            {
                if (!firstEntered.Task.IsCompleted)
                    firstEntered.TrySetResult();
                await release.Task;
                return Results.Ok("done");
            }).RequireRateLimiting("Queued"));

        var client = host.GetTestClient();
        var first = client.GetAsync("/queued");
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = client.GetAsync("/queued");
        // Give the second request time to enter the queue (not rejected).
        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        var third = await client.GetAsync("/queued");
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);

        release.TrySetResult();
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second).StatusCode);
    }

    [Fact]
    public async Task EndpointWithoutPolicy_IsNotThrottled()
    {
        using var host = await CreateHostAsync(o =>
            o.AddFixedWindow("Limited", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.FromMinutes(5);
                p.Partition = PermixaRateLimitPartitionKind.Global;
            }),
            app =>
            {
                app.MapGet("/limited", () => Results.Ok("limited")).RequireRateLimiting("Limited");
                app.MapGet("/open", () => Results.Ok("open"));
            });

        var client = host.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/limited")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/limited")).StatusCode);

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/open")).StatusCode);
    }

    [Fact]
    public async Task WithoutAddPermixaRateLimiting_NoHiddenLimiter()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        var app = builder.Build();
        app.MapGet("/ping", () => Results.Ok("pong"));
        await app.StartAsync();

        var client = app.GetTestClient();
        for (var i = 0; i < 20; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ping")).StatusCode);
    }

    [Fact]
    public async Task MultiplePolicies_AreIndependent()
    {
        using var host = await CreateHostAsync(o =>
        {
            o.AddFixedWindow("A", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.FromMinutes(5);
                p.Partition = PermixaRateLimitPartitionKind.Global;
            });
            o.AddFixedWindow("B", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.FromMinutes(5);
                p.Partition = PermixaRateLimitPartitionKind.Global;
            });
        },
        app =>
        {
            app.MapGet("/a", () => Results.Ok("a")).RequireRateLimiting("A");
            app.MapGet("/b", () => Results.Ok("b")).RequireRateLimiting("B");
        });

        var client = host.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/a")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/a")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/b")).StatusCode);
    }

    [Fact]
    public async Task RemoteIpPartitions_AreIsolated()
    {
        using var host = await CreateHostAsync(o =>
            o.AddFixedWindow("ByIp", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.FromMinutes(5);
                p.Partition = PermixaRateLimitPartitionKind.RemoteIp;
            }),
            app => app.MapGet("/by-ip", () => Results.Ok("ok")).RequireRateLimiting("ByIp"),
            assignTestRemoteIp: true);

        var client = host.GetTestClient();

        using var reqA1 = new HttpRequestMessage(HttpMethod.Get, "/by-ip");
        reqA1.Headers.Add("X-Test-Remote-Ip", "203.0.113.1");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(reqA1)).StatusCode);

        using var reqA2 = new HttpRequestMessage(HttpMethod.Get, "/by-ip");
        reqA2.Headers.Add("X-Test-Remote-Ip", "203.0.113.1");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(reqA2)).StatusCode);

        using var reqB = new HttpRequestMessage(HttpMethod.Get, "/by-ip");
        reqB.Headers.Add("X-Test-Remote-Ip", "203.0.113.2");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(reqB)).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedUserPartitions_AreIsolated()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        using var host = await CreateHostWithAuthAsync(o =>
            o.AddFixedWindow("ByUser", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.FromMinutes(5);
                p.Partition = PermixaRateLimitPartitionKind.AuthenticatedUserId;
            }),
            app => app.MapGet("/by-user", () => Results.Ok("ok")).RequireRateLimiting("ByUser"));

        var client = host.GetTestClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestRsaKeys.CreateAccessToken(userA));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/by-user")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/by-user")).StatusCode);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestRsaKeys.CreateAccessToken(userB));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/by-user")).StatusCode);
    }

    [Fact]
    public async Task RateLimitedResponse_DoesNotExposeSensitiveBusinessState()
    {
        using var host = await CreateHostAsync(o =>
            o.AddFixedWindow("Secure", p =>
            {
                p.PermitLimit = 1;
                p.Window = TimeSpan.FromMinutes(5);
                p.Partition = PermixaRateLimitPartitionKind.Global;
            }),
            app => app.MapGet("/secure-limit", () => Results.Ok("ok")).RequireRateLimiting("Secure"));

        var client = host.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/secure-limit")).StatusCode);
        var rejected = await client.GetAsync("/secure-limit");
        var body = await AssertGeneric429Async(rejected);

        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lockout", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mfa", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("challenge", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> AssertGeneric429Async(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(
            PermixaRateLimitingServiceCollectionExtensions.TooManyRequestsCode,
            "RateLimiting.TooManyRequests");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(429, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            PermixaRateLimitingServiceCollectionExtensions.TooManyRequestsCode,
            doc.RootElement.GetProperty("code").GetString());

        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            Assert.True(int.TryParse(values.First(), out var seconds));
            Assert.True(seconds >= 1);
        }

        return body;
    }

    private static async Task<IHost> CreateHostAsync(
        Action<PermixaRateLimitingOptions> configure,
        Action<WebApplication> mapEndpoints,
        bool assignTestRemoteIp = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddPermixaRateLimiting(configure);

        var app = builder.Build();

        if (assignTestRemoteIp)
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Remote-Ip", out var ip)
                    && IPAddress.TryParse(ip.ToString(), out var parsed))
                {
                    context.Connection.RemoteIpAddress = parsed;
                }

                await next();
            });
        }

        app.UseRateLimiter();
        mapEndpoints(app);
        await app.StartAsync();
        return app;
    }

    private static async Task<IHost> CreateHostWithAuthAsync(
        Action<PermixaRateLimitingOptions> configure,
        Action<WebApplication> mapEndpoints)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddPermixaJwtBearer(o =>
        {
            o.Issuer = TestRsaKeys.Issuer;
            o.Audience = TestRsaKeys.Audience;
            o.PublicKeyPem = TestRsaKeys.PublicKeyPem;
        });
        builder.Services.AddPermixaRateLimiting(configure);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseRateLimiter();
        mapEndpoints(app);
        await app.StartAsync();
        return app;
    }
}
