using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Permixa.AspNetCore.Authentication;

public static class PermixaJwtBearerServiceCollectionExtensions
{
    public const string AuthenticationUnauthorizedCode = "Authentication.Unauthorized";
    public const string AuthorizationForbiddenCode = "Authorization.Forbidden";

    /// <summary>
    /// Registers JwtBearer validation (public key only) as the default authenticate/challenge scheme,
    /// plus scoped <see cref="Security.ICurrentUser"/>. Call after Infrastructure issuance registration
    /// as needed; keep private signing keys out of these options.
    /// Host pipeline: <c>UseExceptionHandler</c> → <c>UseAuthentication</c> → <c>UseAuthorization</c>.
    /// </summary>
    public static IServiceCollection AddPermixaJwtBearer(
        this IServiceCollection services,
        Action<PermixaJwtBearerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PermixaJwtBearerOptions();
        configure(options);
        Validate(options);

        services.AddSingleton(Options.Create(Clone(options)));
        services.AddHttpContextAccessor();
        services.AddScoped<Security.ICurrentUser, Security.HttpContextCurrentUser>();

        using (var rsa = RsaPublicKeyLoader.LoadPublicKey(options.PublicKeyPem!))
        {
            var securityKey = new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: false));

            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, jwt =>
                {
                    jwt.MapInboundClaims = false;
                    jwt.IncludeErrorDetails = false;
                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = options.Issuer,
                        ValidateAudience = true,
                        ValidAudience = options.Audience,
                        ValidateLifetime = true,
                        RequireExpirationTime = true,
                        ValidateIssuerSigningKey = true,
                        RequireSignedTokens = true,
                        ClockSkew = options.ClockSkew,
                        IssuerSigningKey = securityKey,
                        ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }
                    };

                    jwt.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = context =>
                        {
                            var sub = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                            if (string.IsNullOrWhiteSpace(sub) || !Guid.TryParse(sub, out _))
                            {
                                context.Fail("Permixa access token requires a valid Guid sub claim.");
                            }

                            return Task.CompletedTask;
                        },
                        OnChallenge = async context =>
                        {
                            if (context.Response.HasStarted)
                                return;

                            // HandleResponse suppresses the default JwtBearer writer; restore the
                            // standards-compliant Bearer challenge without leaking validation internals.
                            context.HandleResponse();
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;

                            var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
                            {
                                Status = StatusCodes.Status401Unauthorized,
                                Title = "Unauthorized",
                                Detail = "Authentication is required.",
                                Extensions =
                                {
                                    ["code"] = AuthenticationUnauthorizedCode,
                                    ["traceId"] = Permixa.AspNetCore.ProblemDetails.TraceIds.Resolve(context.HttpContext)
                                }
                            };

                            await WriteProblemAsync(context.Response, problem);
                        },
                        OnForbidden = async context =>
                        {
                            if (context.Response.HasStarted)
                                return;

                            context.Response.StatusCode = StatusCodes.Status403Forbidden;

                            var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
                            {
                                Status = StatusCodes.Status403Forbidden,
                                Title = "Forbidden",
                                Detail = "The current user is not authorized for this resource.",
                                Extensions =
                                {
                                    ["code"] = AuthorizationForbiddenCode,
                                    ["traceId"] = Permixa.AspNetCore.ProblemDetails.TraceIds.Resolve(context.HttpContext)
                                }
                            };

                            await WriteProblemAsync(context.Response, problem);
                        }
                    };
                });
        }

        return services;
    }

    private static void Validate(PermixaJwtBearerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer))
            throw new InvalidOperationException("Permixa JWT Bearer Issuer is required.");

        if (string.IsNullOrWhiteSpace(options.Audience))
            throw new InvalidOperationException("Permixa JWT Bearer Audience is required.");

        if (string.IsNullOrWhiteSpace(options.PublicKeyPem))
            throw new InvalidOperationException("Permixa JWT Bearer PublicKeyPem is required.");

        if (options.ClockSkew < TimeSpan.Zero)
            throw new InvalidOperationException("Permixa JWT Bearer ClockSkew cannot be negative.");

        using var rsa = RsaPublicKeyLoader.LoadPublicKey(options.PublicKeyPem);
    }

    private static PermixaJwtBearerOptions Clone(PermixaJwtBearerOptions source) =>
        new()
        {
            Issuer = source.Issuer,
            Audience = source.Audience,
            PublicKeyPem = source.PublicKeyPem,
            ClockSkew = source.ClockSkew
        };

    private static Task WriteProblemAsync(
        HttpResponse response,
        Microsoft.AspNetCore.Mvc.ProblemDetails problem) =>
        response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json");
}
