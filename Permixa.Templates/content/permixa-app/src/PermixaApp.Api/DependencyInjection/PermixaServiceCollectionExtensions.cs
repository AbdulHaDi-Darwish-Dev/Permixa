using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PermixaApp.Api.Hosting;
using Permixa.AspNetCore.Authentication;
using Permixa.AspNetCore.Authorization;
using Permixa.AspNetCore.ProblemDetails;
using Permixa.AspNetCore.RateLimiting;
using Permixa.Infrastructure;
#if (redis)
using Permixa.Caching.Redis;
#endif
#if (resend)
using Permixa.Email.Resend;
#endif

namespace PermixaApp.Api.DependencyInjection;

/// <summary>
/// Host-local composition of existing Permixa NuGet registration APIs.
/// Not a Permixa framework API — lives in the generated application only.
/// </summary>
public static class PermixaServiceCollectionExtensions
{
    public static IServiceCollection AddPermixaHost(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string connectionString)
    {
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString = connectionString;
            o.Bootstrap.Enabled = configuration.GetValue("Permixa:Bootstrap:Enabled", false);
            o.Bootstrap.OwnerEmail = configuration["Permixa:Bootstrap:OwnerEmail"];
            o.Bootstrap.OwnerUserName = configuration["Permixa:Bootstrap:OwnerUserName"];
            o.Bootstrap.OwnerPassword = configuration["Permixa:Bootstrap:OwnerPassword"];
        });

        services.AddPermixaAuthentication(o =>
        {
            o.Authentication.RequireConfirmedEmail =
                configuration.GetValue("Permixa:Authentication:RequireConfirmedEmail", false);
            o.Jwt.Issuer = RequireConfig(configuration, "Permixa:Jwt:Issuer");
            o.Jwt.Audience = RequireConfig(configuration, "Permixa:Jwt:Audience");
            o.Jwt.PrivateKeyPem = RequireConfig(configuration, "Permixa:Jwt:PrivateKeyPem");
        });

        services.AddPermixaAuthorization();

        // Required whenever AddPermixaAuthorization is used: admin email-change / force-password-reset
        // use cases depend on verification services. Email *delivery* (Resend) remains optional below.
        services.AddPermixaVerification();
#if (!resend)
        // Without email delivery, still register a dispatcher so Development DI validation succeeds.
        services.AddSingleton<Permixa.Application.Verification.Abstractions.IVerificationDispatcher,
            UnconfiguredVerificationDispatcher>();
#endif

#if (resend)
        services.AddPermixaEmailDelivery(o =>
        {
            o.FromEmail = RequireConfig(configuration, "Permixa:Email:FromEmail");
            o.FromName = RequireConfig(configuration, "Permixa:Email:FromName");
            o.Branding.ApplicationName = RequireConfig(configuration, "Permixa:Email:ApplicationName");
            o.EmailConfirmationUrlTemplate =
                RequireConfig(configuration, "Permixa:Email:EmailConfirmationUrlTemplate");
            o.PasswordResetUrlTemplate =
                RequireConfig(configuration, "Permixa:Email:PasswordResetUrlTemplate");
        });
        services.AddPermixaResendEmail(o =>
        {
            o.ApiKey = RequireConfig(configuration, "Permixa:Resend:ApiKey");
        });
#endif

#if (redis)
        services.AddPermixaRedisAuthorizationCache(o =>
        {
            o.ConnectionString = configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("Missing ConnectionStrings:Redis for --redis variant.");
        });
#endif

        services.AddPermixaJwtBearer(o =>
        {
            o.Issuer = RequireConfig(configuration, "Permixa:Jwt:Issuer");
            o.Audience = RequireConfig(configuration, "Permixa:Jwt:Audience");
            o.PublicKeyPem = RequireConfig(configuration, "Permixa:Jwt:PublicKeyPem");
        });

        services.AddPermixaPermissionAuthorization();
        services.AddPermixaProblemDetails();

        services.AddPermixaRateLimiting(o =>
        {
            o.AddSlidingWindow("Login", p =>
            {
                p.PermitLimit = 20;
                p.Window = TimeSpan.FromMinutes(1);
                p.Partition = PermixaRateLimitPartitionKind.RemoteIp;
            });
        });

        if (environment.IsDevelopment()
            && configuration.GetValue("Permixa:AppSeed:Enabled", false))
        {
            // Privileged Development initialization only — not a general admin service.
            services.AddScoped<AppPermissionSeeder>();
        }

        return services;
    }

    private static string RequireConfig(IConfiguration configuration, string key) =>
        configuration[key]
        ?? throw new InvalidOperationException(
            $"Missing configuration '{key}'. Use User Secrets / environment / scripts/init-dev-secrets.");
}
