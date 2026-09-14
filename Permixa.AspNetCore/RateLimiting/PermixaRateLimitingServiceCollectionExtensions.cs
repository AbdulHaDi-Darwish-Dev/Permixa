using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Permixa.AspNetCore.ProblemDetails;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.AspNetCore.RateLimiting;

/// <summary>
/// Opt-in registration for Permixa's reusable ASP.NET Core Rate Limiting configuration layer.
/// Does not install a global limiter; hosts must call <c>UseRateLimiter</c> and apply named policies.
/// </summary>
public static class PermixaRateLimitingServiceCollectionExtensions
{
    public const string TooManyRequestsCode = "RateLimiting.TooManyRequests";

    /// <summary>
    /// Registers ASP.NET Core Rate Limiting with developer-defined Permixa policies.
    /// Not called by other <c>AddPermixa*</c> methods — hosts must opt in explicitly.
    /// </summary>
    public static IServiceCollection AddPermixaRateLimiting(
        this IServiceCollection services,
        Action<PermixaRateLimitingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PermixaRateLimitingOptions();
        configure(options);

        if (options.PolicyNames.Count == 0)
        {
            throw new InvalidOperationException(
                "AddPermixaRateLimiting requires at least one policy " +
                "(AddFixedWindow / AddSlidingWindow / AddTokenBucket / AddConcurrency).");
        }

        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiterOptions.OnRejected = OnRejectedAsync;
            options.ApplyTo(rateLimiterOptions);
        });

        return services;
    }

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        if (httpContext.Response.HasStarted)
            return;

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        // Minimal ProblemDetails-shaped body; no account/challenge/security state.
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too Many Requests",
            Detail = "The rate limit for this endpoint has been exceeded. Please try again later.",
            Extensions =
            {
                ["code"] = TooManyRequestsCode,
                ["traceId"] = TraceIds.Resolve(httpContext)
            }
        };

        httpContext.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            problem,
            cancellationToken: cancellationToken);
    }
}
