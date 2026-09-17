using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Permixa.Infrastructure.Email;
using Testcontainers.MsSql;
using Xunit;
#if (redis)
using Testcontainers.Redis;
#endif

namespace PermixaApp.IntegrationTests.Support;

public sealed class CapturedEmail
{
    public required string To { get; init; }
    public required string Subject { get; init; }
    public required string TextBody { get; init; }
}

public sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<CapturedEmail> _sent = new();

    public IReadOnlyList<CapturedEmail> Sent => _sent;

    public void Clear() => _sent.Clear();

    public Task SendAsync(EmailOutgoingMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Add(new CapturedEmail
        {
            To = message.To,
            Subject = message.Subject,
            TextBody = message.TextBody
        });
        return Task.CompletedTask;
    }
}

public class AppWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

#if (redis)
    private RedisContainer? _redis = new RedisBuilder().Build();
#endif

    public CapturingEmailSender Emails { get; } = new();

    protected virtual string EnvironmentName => Environments.Development;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
#if (redis)
        await _redis!.StartAsync();
#endif
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
#if (redis)
        if (_redis is not null)
            await _redis.DisposeAsync();
#endif
    }

#if (redis)
    public async Task StopRedisAsync()
    {
        if (_redis is null)
            return;
        await _redis.DisposeAsync();
        _redis = null;
    }
#endif

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);

        // UseSetting wins over empty appsettings ConnectionStrings:Default.
        builder.UseSetting("ConnectionStrings:Default", _sql.GetConnectionString());
#if (redis)
        builder.UseSetting("ConnectionStrings:Redis", _redis!.GetConnectionString());
#endif
        builder.UseSetting("Permixa:Jwt:Issuer", TestKeys.Issuer);
        builder.UseSetting("Permixa:Jwt:Audience", TestKeys.Audience);
        builder.UseSetting("Permixa:Jwt:PrivateKeyPem", TestKeys.PrivateKeyPem);
        builder.UseSetting("Permixa:Jwt:PublicKeyPem", TestKeys.PublicKeyPem);
        builder.UseSetting("Permixa:Bootstrap:Enabled", "true");
        builder.UseSetting("Permixa:Bootstrap:OwnerEmail", TestKeys.OwnerEmail);
        builder.UseSetting("Permixa:Bootstrap:OwnerUserName", TestKeys.OwnerUserName);
        builder.UseSetting("Permixa:Bootstrap:OwnerPassword", TestKeys.OwnerPassword);
        builder.UseSetting("Permixa:AppSeed:Enabled", "true");
#if (resend)
        builder.UseSetting("Permixa:Authentication:RequireConfirmedEmail", "true");
        builder.UseSetting("Permixa:Email:FromEmail", "noreply@example.test");
        builder.UseSetting("Permixa:Email:FromName", "App Tests");
        builder.UseSetting("Permixa:Email:ApplicationName", "App");
        builder.UseSetting(
            "Permixa:Email:EmailConfirmationUrlTemplate",
            "https://example.test/verify?challengeId={challengeId}&token={token}");
        builder.UseSetting(
            "Permixa:Email:PasswordResetUrlTemplate",
            "https://example.test/reset?challengeId={challengeId}&token={token}");
        builder.UseSetting("Permixa:Resend:ApiKey", "re_test_not_used");
#else
        builder.UseSetting("Permixa:Authentication:RequireConfirmedEmail", "false");
#endif

#if (resend)
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddScoped<IEmailSender>(_ => Emails);
        });
#endif
    }

    public HttpClient CreateAuthenticatedClient(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}

public sealed class AuthTokenResponse
{
    public Guid UserId { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}

public static class AuthClientExtensions
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<AuthTokenResponse> LoginAsOwnerAsync(this HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            emailOrUserName = TestKeys.OwnerEmail,
            password = TestKeys.OwnerPassword
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokenResponse>(Json))!;
    }
}
