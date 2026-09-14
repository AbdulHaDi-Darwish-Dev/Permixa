using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Login;
using Permixa.Application.Authentication.Logout;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Refresh;
using Permixa.Application.Authentication.Register;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Permissions.Create;
using Permixa.Application.Authorization.RolePermissions.Assign;
using Permixa.Application.Authorization.RolePermissions.Remove;
using Permixa.Application.Authorization.UserPermissionOverrides.Set;
using Permixa.Application.Common;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Verification.EmailConfirmation;
using Permixa.Application.Verification.PasswordReset;
using Permixa.AspNetCore.Authentication;
using Permixa.AspNetCore.Authorization;
using Permixa.AspNetCore.Http;
using Permixa.AspNetCore.ProblemDetails;
using Permixa.AspNetCore.Security;
using Permixa.Domain.Authorization;
using Permixa.Domain.Common;
using Permixa.Domain.Verification;
using Permixa.Infrastructure;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Email;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Permixa.Caching.Redis;
using Permixa.Email.Resend;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Permixa.IntegrationTests.Support;

public sealed class PermixaTestHostOptions
{
    public bool RequireConfirmedEmail { get; set; }

    public bool ThrowOnEmail { get; set; }

    public bool UseRedis { get; set; } = true;

    public string? RedisConnectionString { get; set; }
}

public sealed class PermixaTestHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private PermixaTestHost(
        WebApplication app,
        HttpClient client,
        CapturedEmailStore emails,
        TestClock clock,
        CapturingLoggerProvider logs,
        CountingPermissionCache? cache,
        string connectionString,
        string redisKeyPrefix)
    {
        App = app;
        Client = client;
        Emails = emails;
        Clock = clock;
        Logs = logs;
        Cache = cache;
        ConnectionString = connectionString;
        RedisKeyPrefix = redisKeyPrefix;
    }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public CapturedEmailStore Emails { get; }

    public TestClock Clock { get; }

    public CapturingLoggerProvider Logs { get; }

    public CountingPermissionCache? Cache { get; }

    public string ConnectionString { get; }

    public string RedisKeyPrefix { get; }

    public IServiceProvider Services => App.Services;

    public static async Task<PermixaTestHost> StartAsync(
        SqlServerContainerFixture sql,
        RedisContainerFixture redis,
        Action<PermixaTestHostOptions>? configure = null)
    {
        var options = new PermixaTestHostOptions();
        configure?.Invoke(options);

        var connectionString = sql.CreateUniqueDatabaseConnectionString();
        var clock = new TestClock();
        var emails = new CapturedEmailStore();
        var logs = new CapturingLoggerProvider();
        var redisKeyPrefix = $"permixa:authz:{Guid.NewGuid():N}:";
        CountingPermissionCache? countingCache = null;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(PermixaTestHost).Assembly.FullName,
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString = connectionString;
            o.Bootstrap.Enabled = true;
            o.Bootstrap.OwnerEmail = TestKeys.OwnerEmail;
            o.Bootstrap.OwnerUserName = TestKeys.OwnerUserName;
            o.Bootstrap.OwnerPassword = TestKeys.OwnerPassword;
        });
        builder.Services.AddSingleton(clock);
        builder.Services.AddSingleton<IClock>(clock);

        builder.Services.AddPermixaAuthentication(o =>
        {
            o.Authentication.RequireConfirmedEmail = options.RequireConfirmedEmail;
            o.Jwt.Issuer = TestKeys.Issuer;
            o.Jwt.Audience = TestKeys.Audience;
            o.Jwt.PrivateKeyPem = TestKeys.PrivateKeyPem;
        });
        builder.Services.AddPermixaVerification();
        builder.Services.AddPermixaEmailDelivery(o =>
        {
            o.FromEmail = "noreply@permixa.test";
            o.FromName = "Permixa Tests";
            o.EmailConfirmationUrlTemplate = TestKeys.EmailConfirmationUrlTemplate;
            o.PasswordResetUrlTemplate = TestKeys.PasswordResetUrlTemplate;
            o.Branding.ApplicationName = "Permixa";
        });
        builder.Services.AddPermixaResendEmail(o =>
        {
            o.ApiKey = TestKeys.FakeResendApiKey;
        });

        if (options.ThrowOnEmail)
            builder.Services.AddScoped<IEmailSender, ThrowingEmailSender>();
        else
            builder.Services.AddScoped<IEmailSender>(_ => new CapturingEmailSender(emails));

        if (options.UseRedis)
        {
            builder.Services.AddPermixaRedisAuthorizationCache(o =>
            {
                o.ConnectionString = options.RedisConnectionString ?? redis.ConnectionString;
                o.KeyPrefix = redisKeyPrefix;
            });
            builder.Services.AddSingleton(sp =>
            {
                var inner = new RedisPermissionCache(
                    sp.GetRequiredService<IConnectionMultiplexer>(),
                    sp.GetRequiredService<IOptions<PermixaRedisAuthorizationCacheOptions>>(),
                    sp.GetRequiredService<ILogger<RedisPermissionCache>>());
                return new CountingPermissionCache(inner);
            });
            builder.Services.AddSingleton<IPermissionCache>(sp =>
                sp.GetRequiredService<CountingPermissionCache>());
        }

        builder.Services.AddPermixaJwtBearer(o =>
        {
            o.Issuer = TestKeys.Issuer;
            o.Audience = TestKeys.Audience;
            o.PublicKeyPem = TestKeys.PublicKeyPem;
        });
        builder.Services.AddPermixaPermissionAuthorization();
        builder.Services.AddPermixaProblemDetails();
        builder.Services.AddPermixaAuthorization();

        var app = builder.Build();
        countingCache = options.UseRedis
            ? app.Services.GetRequiredService<CountingPermissionCache>()
            : null;

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
        }

        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        MapTestEndpoints(app);

        await app.StartAsync();

        return new PermixaTestHost(
            app,
            app.GetTestClient(),
            emails,
            clock,
            logs,
            countingCache,
            connectionString,
            redisKeyPrefix);
    }

    public Task BootstrapAsync()
    {
        return ExecuteScopedAsync(sp =>
            sp.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync());
    }

    public async Task<T> ExecuteScopedAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public async Task ExecuteScopedAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    public async Task<RegisterResult> RegisterAsync(string userName, string email, string password)
    {
        var response = await Client.PostAsJsonAsync("/register", new RegisterRequest
        {
            UserName = userName,
            Email = email,
            Password = password
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RegisterResult>(Json))!;
    }

    public async Task<HttpResponseMessage> RegisterRawAsync(string userName, string email, string password) =>
        await Client.PostAsJsonAsync("/register", new RegisterRequest
        {
            UserName = userName,
            Email = email,
            Password = password
        });

    public async Task<AuthenticationResult> LoginAsync(string emailOrUserName, string password)
    {
        var response = await Client.PostAsJsonAsync("/login", new LoginRequest
        {
            EmailOrUserName = emailOrUserName,
            Password = password
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthenticationResult>(Json))!;
    }

    public Task<HttpResponseMessage> LoginRawAsync(string emailOrUserName, string password) =>
        Client.PostAsJsonAsync("/login", new LoginRequest
        {
            EmailOrUserName = emailOrUserName,
            Password = password
        });

    public HttpClient AuthenticatedClient(string accessToken)
    {
        var client = App.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public async Task<(Guid UserId, string Email, Guid RoleId)> RegisterUserInRoleAsync(
        string userName,
        string roleName,
        int roleLevel)
    {
        var email = $"{userName}@permixa.test";
        var registered = await RegisterAsync(userName, email, TestKeys.UserPassword);

        await ExecuteScopedAsync(async sp =>
        {
            var roles = sp.GetRequiredService<RoleManager<ApplicationRole>>();
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            if (await roles.FindByNameAsync(roleName) is null)
                Assert.True((await roles.CreateAsync(new ApplicationRole(roleName, roleLevel))).Succeeded);

            var user = await users.FindByIdAsync(registered.UserId.ToString());
            Assert.NotNull(user);
            Assert.True((await users.AddToRoleAsync(user!, roleName)).Succeeded);
        });

        var roleId = await ExecuteScopedAsync(async sp =>
        {
            var roles = sp.GetRequiredService<RoleManager<ApplicationRole>>();
            var role = await roles.FindByNameAsync(roleName);
            return role!.Id;
        });

        return (registered.UserId, email, roleId);
    }

    public Task<Guid> CreatePermissionAsync(string name) =>
        ExecuteScopedAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var owner = await users.FindByEmailAsync(TestKeys.OwnerEmail);
            Assert.NotNull(owner);

            var useCase = sp.GetRequiredService<CreatePermissionUseCase>();
            var result = await useCase.ExecuteAsync(new CreatePermissionCommand(owner.Id, name, null));
            Assert.True(result.IsSuccess, result.Error?.Description);
            return result.Value.Id;
        });

    public Task AssignPermissionToRoleAsync(Guid actorUserId, Guid roleId, Guid permissionId) =>
        ExecuteScopedAsync(async sp =>
        {
            var useCase = sp.GetRequiredService<AssignPermissionToRoleUseCase>();
            var result = await useCase.ExecuteAsync(
                new AssignPermissionToRoleCommand(actorUserId, roleId, permissionId));
            Assert.True(result.IsSuccess, result.Error?.Description);
        });

    public Task<Result> AssignPermissionToRoleResultAsync(Guid actorUserId, Guid roleId, Guid permissionId) =>
        ExecuteScopedAsync(sp =>
            sp.GetRequiredService<AssignPermissionToRoleUseCase>()
                .ExecuteAsync(new AssignPermissionToRoleCommand(actorUserId, roleId, permissionId)));

    public Task RemovePermissionFromRoleAsync(Guid actorUserId, Guid roleId, Guid permissionId) =>
        ExecuteScopedAsync(async sp =>
        {
            var useCase = sp.GetRequiredService<RemovePermissionFromRoleUseCase>();
            var result = await useCase.ExecuteAsync(
                new RemovePermissionFromRoleCommand(actorUserId, roleId, permissionId));
            Assert.True(result.IsSuccess, result.Error?.Description);
        });

    public Task SetOverrideAsync(
        Guid actorUserId,
        Guid targetUserId,
        Guid permissionId,
        PermissionEffect effect) =>
        ExecuteScopedAsync(async sp =>
        {
            var useCase = sp.GetRequiredService<SetUserPermissionOverrideUseCase>();
            var result = await useCase.ExecuteAsync(
                new SetUserPermissionOverrideCommand(actorUserId, targetUserId, permissionId, effect));
            Assert.True(result.IsSuccess, result.Error?.Description);
        });

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.DisposeAsync();
    }

    private static void MapTestEndpoints(WebApplication app)
    {
        app.MapPost("/register", async (
            RegisterRequest request,
            RegisterUserUseCase useCase,
            HttpContext http) =>
        {
            var result = await useCase.ExecuteAsync(request);
            return result.ToHttpResult(http, value => Results.Json(value, statusCode: StatusCodes.Status201Created));
        });

        app.MapPost("/login", async (
            LoginRequest request,
            LoginUseCase useCase,
            HttpContext http) =>
        {
            var result = await useCase.ExecuteAsync(request);
            return result.ToHttpResult(http, value =>
                value.Authentication is not null
                    ? Results.Json(value.Authentication)
                    : Results.Json(value.Mfa));
        });

        app.MapPost("/refresh", async (
            RefreshTokenRequest request,
            RefreshAccessTokenUseCase useCase,
            HttpContext http) =>
        {
            var result = await useCase.ExecuteAsync(request);
            return result.ToHttpResult(http, value => Results.Json(value));
        });

        app.MapPost("/logout", async (
            RevokeRefreshTokenRequest request,
            LogoutUseCase useCase,
            HttpContext http) =>
        {
            var result = await useCase.ExecuteAsync(request);
            return result.ToHttpResult(http, () => Results.Ok());
        });

        app.MapPost("/email-confirmation/request", async (
            RequestEmailConfirmationHttpRequest request,
            RequestEmailConfirmationUseCase useCase,
            HttpContext http) =>
        {
            if (!Enum.TryParse<VerificationMethod>(request.Method, ignoreCase: true, out var method))
                method = (VerificationMethod)(-1);

            var result = await useCase.ExecuteAsync(new RequestEmailConfirmationRequest
            {
                UserId = request.UserId,
                Method = method
            });
            return result.ToHttpResult(http, value => Results.Json(value));
        });

        app.MapPost("/email-confirmation/confirm", async (
            ConfirmEmailRequest request,
            ConfirmEmailUseCase useCase,
            HttpContext http) =>
        {
            var result = await useCase.ExecuteAsync(request);
            return result.ToHttpResult(http, () => Results.Ok());
        });

        app.MapPost("/password-reset/request", async (
            RequestPasswordResetRequest request,
            RequestPasswordResetUseCase useCase,
            HttpContext http) =>
        {
            var result = await useCase.ExecuteAsync(request);
            return result.ToHttpResult(http, () => Results.Json(new { accepted = true }));
        });

        app.MapPost("/password-reset/reset", async (
            ResetPasswordWithVerificationRequest request,
            ResetPasswordWithVerificationUseCase useCase,
            HttpContext http) =>
        {
            var result = await useCase.ExecuteAsync(request);
            return result.ToHttpResult(http, () => Results.Ok());
        });

        app.MapGet("/secure/orders-read", () => Results.Json(new { ok = true }))
            .RequirePermission("Orders.Read");

        app.MapGet("/secure/iam-read", (ICurrentUser user) =>
                Results.Json(new { ok = true, userId = user.UserId }))
            .RequirePermission(IamPermissions.Permissions.Read);

        app.MapGet("/me", (ICurrentUser user) =>
                Results.Json(new { user.IsAuthenticated, userId = user.UserId }))
            .RequireAuthorization();

        app.MapGet("/test/result/{kind}", (string kind, HttpContext http) =>
        {
            var error = kind.ToLowerInvariant() switch
            {
                "validation" => Error.Validation("Test.Validation", "validation detail"),
                "failure" => Error.Failure("Test.Failure", "failure detail"),
                "notfound" => Error.NotFound("Test.NotFound", "not found detail"),
                "conflict" => Error.Conflict("Test.Conflict", "conflict detail"),
                "unauthorized" => Error.Unauthorized("Test.Unauthorized", "unauthorized detail"),
                "forbidden" => Error.Forbidden("Test.Forbidden", "forbidden detail"),
                _ => Error.Failure("Test.Unknown", "unknown")
            };
            return error.ToProblemDetails(http);
        });

        app.MapGet("/test/exception/{kind}", (string kind) =>
        {
            throw kind.ToLowerInvariant() switch
            {
                "concurrency" => new ConcurrencyConflictException("db conflict boom"),
                "domain" => new DomainException("domain boom"),
                "email" => new EmailDeliveryException("smtp boom"),
                _ => new InvalidOperationException("unexpected boom")
            };
        });
    }
}

internal sealed class RequestEmailConfirmationHttpRequest
{
    public Guid UserId { get; set; }

    public string Method { get; set; } = "Otp";
}
