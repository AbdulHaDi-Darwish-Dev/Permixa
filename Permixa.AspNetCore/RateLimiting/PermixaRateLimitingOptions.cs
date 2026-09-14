using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Permixa.AspNetCore.RateLimiting;

/// <summary>
/// Collects named rate-limit policies registered via type-safe algorithm builders.
/// Policy names are developer-defined and used unchanged with ASP.NET Core
/// <c>RequireRateLimiting</c> / <c>EnableRateLimiting</c>.
/// </summary>
public sealed class PermixaRateLimitingOptions
{
    private readonly Dictionary<string, Action<RateLimiterOptions>> _policies =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a Fixed Window policy under <paramref name="policyName"/>.
    /// Duplicate names throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public PermixaRateLimitingOptions AddFixedWindow(
        string policyName,
        Action<FixedWindowRateLimitPolicyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var name = NormalizePolicyName(policyName);
        EnsureUnique(name);

        var options = new FixedWindowRateLimitPolicyOptions();
        configure(options);
        PermixaRateLimitingValidation.ValidateFixedWindow(name, options);

        _policies.Add(name, rateLimiter =>
        {
            rateLimiter.AddPolicy(name, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolvePartitionKey(httpContext, options.Partition),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = options.Window,
                        QueueLimit = options.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));
        });

        return this;
    }

    /// <summary>
    /// Registers a Sliding Window policy under <paramref name="policyName"/>.
    /// Duplicate names throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public PermixaRateLimitingOptions AddSlidingWindow(
        string policyName,
        Action<SlidingWindowRateLimitPolicyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var name = NormalizePolicyName(policyName);
        EnsureUnique(name);

        var options = new SlidingWindowRateLimitPolicyOptions();
        configure(options);
        PermixaRateLimitingValidation.ValidateSlidingWindow(name, options);

        _policies.Add(name, rateLimiter =>
        {
            rateLimiter.AddPolicy(name, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    ResolvePartitionKey(httpContext, options.Partition),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = options.Window,
                        SegmentsPerWindow = options.SegmentsPerWindow,
                        QueueLimit = options.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));
        });

        return this;
    }

    /// <summary>
    /// Registers a Token Bucket policy under <paramref name="policyName"/>.
    /// Duplicate names throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public PermixaRateLimitingOptions AddTokenBucket(
        string policyName,
        Action<TokenBucketRateLimitPolicyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var name = NormalizePolicyName(policyName);
        EnsureUnique(name);

        var options = new TokenBucketRateLimitPolicyOptions();
        configure(options);
        PermixaRateLimitingValidation.ValidateTokenBucket(name, options);

        _policies.Add(name, rateLimiter =>
        {
            rateLimiter.AddPolicy(name, httpContext =>
                RateLimitPartition.GetTokenBucketLimiter(
                    ResolvePartitionKey(httpContext, options.Partition),
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = options.TokenLimit,
                        TokensPerPeriod = options.TokensPerPeriod,
                        ReplenishmentPeriod = options.ReplenishmentPeriod,
                        QueueLimit = options.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = options.AutoReplenishment
                    }));
        });

        return this;
    }

    /// <summary>
    /// Registers a Concurrency policy under <paramref name="policyName"/>.
    /// Duplicate names throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public PermixaRateLimitingOptions AddConcurrency(
        string policyName,
        Action<ConcurrencyRateLimitPolicyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var name = NormalizePolicyName(policyName);
        EnsureUnique(name);

        var options = new ConcurrencyRateLimitPolicyOptions();
        configure(options);
        PermixaRateLimitingValidation.ValidateConcurrency(name, options);

        _policies.Add(name, rateLimiter =>
        {
            rateLimiter.AddPolicy(name, httpContext =>
                RateLimitPartition.GetConcurrencyLimiter(
                    ResolvePartitionKey(httpContext, options.Partition),
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        QueueLimit = options.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));
        });

        return this;
    }

    internal IReadOnlyCollection<string> PolicyNames => _policies.Keys;

    internal void ApplyTo(RateLimiterOptions rateLimiterOptions)
    {
        foreach (var apply in _policies.Values)
            apply(rateLimiterOptions);
    }

    private void EnsureUnique(string policyName)
    {
        if (_policies.ContainsKey(policyName))
        {
            throw new InvalidOperationException(
                $"A Permixa rate-limit policy named '{policyName}' is already registered. " +
                "Duplicate policy names are not allowed.");
        }
    }

    private static string NormalizePolicyName(string policyName)
    {
        if (string.IsNullOrWhiteSpace(policyName))
        {
            throw new ArgumentException(
                "Rate-limit policy name must be a non-empty string.",
                nameof(policyName));
        }

        return policyName.Trim();
    }

    internal static string ResolvePartitionKey(
        HttpContext httpContext,
        PermixaRateLimitPartitionKind partition)
    {
        return partition switch
        {
            PermixaRateLimitPartitionKind.Global => "global",
            PermixaRateLimitPartitionKind.RemoteIp => ResolveRemoteIpKey(httpContext),
            PermixaRateLimitPartitionKind.AuthenticatedUserId => ResolveUserIdKey(httpContext),
            _ => throw new InvalidOperationException(
                $"Unsupported rate-limit partition kind '{partition}'.")
        };
    }

    private static string ResolveRemoteIpKey(HttpContext httpContext)
    {
        var ip = httpContext.Connection.RemoteIpAddress;
        return ip is null
            ? "unknown-ip"
            : ip.ToString();
    }

    private static string ResolveUserIdKey(HttpContext httpContext)
    {
        var principal = httpContext.User;
        if (principal?.Identity?.IsAuthenticated == true)
        {
            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!string.IsNullOrWhiteSpace(sub))
            {
                // Normalize Guid-shaped ids for stable keys; keep other stable claim values as-is.
                if (Guid.TryParse(sub, out var userId))
                    return "user:" + userId.ToString("D", CultureInfo.InvariantCulture);

                return "user:" + sub.Trim();
            }
        }

        return "anon:" + ResolveRemoteIpKey(httpContext);
    }
}
