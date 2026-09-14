using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common;
using Permixa.Application.Common.Results;
using Permixa.AspNetCore.Authentication;
using Permixa.AspNetCore.Authorization;
using Permixa.AspNetCore.ProblemDetails;
using Permixa.AspNetCore.Security;
using Permixa.Domain.Common;
using Permixa.Infrastructure.Email;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Permixa.AspNetCore.Tests;

public sealed class JwtBearerIntegrationTests
{
    [Fact]
    public async Task NoToken_Returns401_WithProblemDetailsCode()
    {
        using var host = await CreateHostAsync(_ => true);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/secure");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains(PermixaJwtBearerServiceCollectionExtensions.AuthenticationUnauthorizedCode, json);
    }

    [Fact]
    public async Task MalformedJwt_Returns401()
    {
        using var host = await CreateHostAsync(_ => true);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        var response = await client.GetAsync("/secure");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredJwt_Returns401()
    {
        using var host = await CreateHostAsync(_ => true);
        var client = AuthenticatedClient(host, TestRsaKeys.CreateAccessToken(Guid.NewGuid(), lifetime: TimeSpan.FromMinutes(-5)));

        var response = await client.GetAsync("/secure");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongSignature_Returns401()
    {
        using var other = RSA.Create(2048);
        var token = TestRsaKeys.CreateAccessToken(
            Guid.NewGuid(),
            privatePem: other.ExportPkcs8PrivateKeyPem());

        using var host = await CreateHostAsync(_ => true);
        var client = AuthenticatedClient(host, token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/secure")).StatusCode);
    }

    [Fact]
    public async Task WrongIssuer_Returns401()
    {
        using var host = await CreateHostAsync(_ => true);
        var client = AuthenticatedClient(host, TestRsaKeys.CreateAccessToken(Guid.NewGuid(), issuer: "other-issuer"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/secure")).StatusCode);
    }

    [Fact]
    public async Task WrongAudience_Returns401()
    {
        using var host = await CreateHostAsync(_ => true);
        var client = AuthenticatedClient(host, TestRsaKeys.CreateAccessToken(Guid.NewGuid(), audience: "other-aud"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/secure")).StatusCode);
    }

    [Fact]
    public async Task MissingSub_Returns401()
    {
        using var host = await CreateHostAsync(_ => true);
        var client = AuthenticatedClient(host, TestRsaKeys.CreateAccessToken(null, includeSub: false));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/secure")).StatusCode);
    }

    [Fact]
    public async Task MalformedSub_Returns401()
    {
        using var host = await CreateHostAsync(_ => true);
        var client = AuthenticatedClient(host, TestRsaKeys.CreateAccessToken(null, rawSub: "not-a-guid"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/secure")).StatusCode);
    }

    [Fact]
    public async Task ValidToken_WithPermission_Succeeds()
    {
        var userId = Guid.NewGuid();
        using var host = await CreateHostAsync(p => p == "Orders.Read");
        var client = AuthenticatedClient(host, TestRsaKeys.CreateAccessToken(userId));

        var response = await client.GetAsync("/secure");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(userId.ToString("D"), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ValidToken_WithoutPermission_Returns403()
    {
        using var host = await CreateHostAsync(_ => false);
        var client = AuthenticatedClient(host, TestRsaKeys.CreateAccessToken(Guid.NewGuid()));

        var response = await client.GetAsync("/secure");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains(PermixaJwtBearerServiceCollectionExtensions.AuthorizationForbiddenCode, json);
    }

    private static HttpClient AuthenticatedClient(IHost host, string token)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    internal static async Task<IHost> CreateHostAsync(
        Func<string, bool> hasPermission,
        Action<IEndpointRouteBuilder>? mapExtra = null)
    {
        var permissions = new Mock<IEffectivePermissionService>();
        permissions
            .Setup(p => p.HasPermissionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string name, CancellationToken _) => Result.Success(hasPermission(name)));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();

        builder.Services.AddLogging();
        builder.Services.AddPermixaJwtBearer(o =>
        {
            o.Issuer = TestRsaKeys.Issuer;
            o.Audience = TestRsaKeys.Audience;
            o.PublicKeyPem = TestRsaKeys.PublicKeyPem;
            o.ClockSkew = TimeSpan.FromSeconds(30);
        });
        builder.Services.AddPermixaPermissionAuthorization();
        builder.Services.AddPermixaProblemDetails();
        builder.Services.AddSingleton(permissions.Object);

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/secure", (ICurrentUser user) => Results.Text(user.UserId!.Value.ToString("D")))
            .RequirePermission("Orders.Read");

        app.MapGet("/secure-mvc-style", () => Results.Ok("ok"))
            .RequireAuthorization(PermixaPermissionPolicies.CreatePolicyName("Orders.Read"));

        mapExtra?.Invoke(app);

        await app.StartAsync();
        return app;
    }
}

public sealed class PermissionAuthorizationSurfaceTests
{
    [Fact]
    public async Task MinimalApi_RequirePermission_And_AttributePolicy_BothEnforce()
    {
        using var host = await JwtBearerIntegrationTests.CreateHostAsync(p => p == "Orders.Read");
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestRsaKeys.CreateAccessToken(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/secure")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/secure-mvc-style")).StatusCode);
    }

    [Fact]
    public async Task StackedPermissions_UseAndSemantics()
    {
        var granted = new HashSet<string>(StringComparer.Ordinal) { "Orders.Read" };
        using var host = await JwtBearerIntegrationTests.CreateHostAsync(
            granted.Contains,
            app =>
            {
                app.MapGet("/both", () => Results.Ok("both"))
                    .RequirePermission("Orders.Read")
                    .RequirePermission("Orders.Approve");
            });

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestRsaKeys.CreateAccessToken(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/both")).StatusCode);

        granted.Add("Orders.Approve");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/both")).StatusCode);
    }

    [Fact]
    public async Task HostDefinedPolicy_StillWorks_ViaFallbackProvider()
    {
        using var host = await CreateHostWithHostPolicyAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestRsaKeys.CreateAccessToken(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/host-policy")).StatusCode);
    }

    [Fact]
    public void MalformedRequirePermission_FailsFast()
    {
        Assert.Throws<InvalidOperationException>(() => new RequirePermissionAttribute(""));
        Assert.Throws<InvalidOperationException>(() => new RequirePermissionAttribute("BadName"));
        Assert.Throws<InvalidOperationException>(() =>
            PermixaPermissionPolicies.CreatePolicyName("not-valid"));
    }

    [Fact]
    public void ValidPolicyName_IsProduced()
    {
        var policy = PermixaPermissionPolicies.CreatePolicyName("Orders.Read");
        Assert.Equal("Permixa.Permission:Orders.Read", policy);
    }

    private static async Task<IHost> CreateHostWithHostPolicyAsync()
    {
        var permissions = new Mock<IEffectivePermissionService>();
        permissions
            .Setup(p => p.HasPermissionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddPermixaJwtBearer(o =>
        {
            o.Issuer = TestRsaKeys.Issuer;
            o.Audience = TestRsaKeys.Audience;
            o.PublicKeyPem = TestRsaKeys.PublicKeyPem;
        });
        builder.Services.AddPermixaPermissionAuthorization();
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("SomeHostPolicy", p => p.RequireAuthenticatedUser());
        });
        builder.Services.AddSingleton(permissions.Object);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/host-policy", () => Results.Ok("host"))
            .RequireAuthorization("SomeHostPolicy");
        await app.StartAsync();
        return app;
    }
}

public sealed class CurrentUserTests
{
    [Fact]
    public async Task Anonymous_IsNotAuthenticated()
    {
        using var host = await JwtBearerIntegrationTests.CreateHostAsync(_ => true);
        var client = host.GetTestClient();

        // Hit an anonymous endpoint registered on a fresh host
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddPermixaJwtBearer(o =>
        {
            o.Issuer = TestRsaKeys.Issuer;
            o.Audience = TestRsaKeys.Audience;
            o.PublicKeyPem = TestRsaKeys.PublicKeyPem;
        });
        var app = builder.Build();
        app.MapGet("/me", (ICurrentUser user) => Results.Json(new
        {
            user.IsAuthenticated,
            user.UserId
        }));
        await app.StartAsync();

        var json = await app.GetTestClient().GetStringAsync("/me");
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("userId").ValueKind);
    }

    [Fact]
    public async Task Authenticated_ExposesUserId()
    {
        var userId = Guid.NewGuid();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddPermixaJwtBearer(o =>
        {
            o.Issuer = TestRsaKeys.Issuer;
            o.Audience = TestRsaKeys.Audience;
            o.PublicKeyPem = TestRsaKeys.PublicKeyPem;
        });
        builder.Services.AddPermixaPermissionAuthorization();
        var permissions = new Mock<IEffectivePermissionService>();
        permissions
            .Setup(p => p.HasPermissionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));
        builder.Services.AddSingleton(permissions.Object);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/me", (ICurrentUser user) => Results.Json(new
        {
            user.IsAuthenticated,
            UserId = user.UserId
        })).RequireAuthorization();
        await app.StartAsync();

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestRsaKeys.CreateAccessToken(userId));
        var json = await client.GetStringAsync("/me");
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal(userId, doc.RootElement.GetProperty("userId").GetGuid());
    }
}

public sealed class ProblemDetailsMappingTests
{
    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Failure, 400)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    public void ErrorType_MapsToExpectedStatus(ErrorType type, int status)
    {
        Assert.Equal(status, PermixaProblemDetailsMapper.MapStatusCode(type));
    }

    [Fact]
    public void FromError_IncludesCodeAndTraceId()
    {
        var http = new DefaultHttpContext();
        http.TraceIdentifier = "trace-123";
        var problem = PermixaProblemDetailsMapper.FromError(
            Error.Conflict("Orders.Conflict", "Conflicted"),
            http);

        Assert.Equal(409, problem.Status);
        Assert.Equal("Orders.Conflict", problem.Extensions["code"]?.ToString());
        Assert.Equal("trace-123", problem.Extensions["traceId"]?.ToString());
        Assert.Equal("Conflicted", problem.Detail);
    }
}

public sealed class ExceptionHandlerIntegrationTests
{
    [Theory]
    [InlineData(typeof(ConcurrencyConflictException), 409, "Concurrency.Conflict")]
    [InlineData(typeof(DomainException), 500, "InternalError")]
    [InlineData(typeof(EmailDeliveryException), 503, "Email.DeliveryFailed")]
    [InlineData(typeof(InvalidOperationException), 500, "InternalError")]
    public async Task UnexpectedExceptions_MapSafely(Type exceptionType, int status, string code)
    {
        Exception ex = exceptionType.Name switch
        {
            nameof(ConcurrencyConflictException) =>
                new ConcurrencyConflictException("db conflict boom"),
            nameof(DomainException) =>
                new DomainException("domain boom"),
            nameof(EmailDeliveryException) =>
                new EmailDeliveryException("smtp boom"),
            _ => new InvalidOperationException("unexpected boom")
        };

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddPermixaProblemDetails();
        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapGet("/boom", () => { throw ex; });
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/boom");
        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(code, body);
        Assert.DoesNotContain("boom", body);
        Assert.Contains("traceId", body);
    }
}

public sealed class HandlerDelegationTests
{
    [Fact]
    public async Task PermissionHandler_CallsEffectivePermissionService()
    {
        var userId = Guid.NewGuid();
        var permissions = new Mock<IEffectivePermissionService>();
        permissions
            .Setup(p => p.HasPermissionAsync(userId, "Orders.Read", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true))
            .Verifiable();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddPermixaJwtBearer(o =>
        {
            o.Issuer = TestRsaKeys.Issuer;
            o.Audience = TestRsaKeys.Audience;
            o.PublicKeyPem = TestRsaKeys.PublicKeyPem;
        });
        builder.Services.AddPermixaPermissionAuthorization();
        builder.Services.AddSingleton(permissions.Object);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/x", () => Results.Ok()).RequirePermission("Orders.Read");
        await app.StartAsync();

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestRsaKeys.CreateAccessToken(userId));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/x")).StatusCode);
        permissions.VerifyAll();
    }
}
