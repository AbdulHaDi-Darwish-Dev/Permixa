# Rate Limiting

## Purpose

Permixa provides a **reusable, type-safe configuration layer** over ASP.NET Core’s native Rate Limiting infrastructure.

The host defines:

1. Policy names
2. Limiter algorithm
3. Limits / windows / replenishment / queue
4. Partition kind
5. Where policies apply (`RequireRateLimiting` / `EnableRateLimiting`)

Permixa does **not** install a global host throttle and does **not** force IAM-specific policy catalogs or algorithm mappings.

## What it protects

HTTP request volume and server resource abuse on endpoints the host explicitly protects.

It complements (does not replace):

| Control | Protects |
|---------|----------|
| Rate limiting | Request volume / flooding on chosen endpoints |
| Identity lockout | Bad password attempts for an account |
| VerificationChallenge limits | OTP / verification attempt lifecycle |
| MFA challenge `MaxAttempts` | Processed MFA attempt lifecycle |

A request rejected by the rate limiter before a use case runs must not mutate lockout, verification, MFA challenge, or refresh-replay state.

## Package placement

Lives in `Permixa.AspNetCore` (future public package brand: Permixa.AspNetCore).

Not a separate `Permixa.RateLimiting` project.

Domain / Application / Infrastructure are unchanged. No EF migrations. No Redis rate-limit storage.

## Opt-in registration

```csharp
builder.Services.AddPermixaRateLimiting(options =>
{
    options.AddFixedWindow("PublicApi", policy =>
    {
        policy.PermitLimit = 100;
        policy.Window = TimeSpan.FromMinutes(1);
    });

    options.AddSlidingWindow("Login", policy =>
    {
        policy.PermitLimit = 5;
        policy.Window = TimeSpan.FromMinutes(1);
        policy.SegmentsPerWindow = 6;
    });

    options.AddTokenBucket("Otp", policy =>
    {
        policy.TokenLimit = 3;
        policy.TokensPerPeriod = 1;
        policy.ReplenishmentPeriod = TimeSpan.FromMinutes(2);
    });

    options.AddConcurrency("HeavyOperation", policy =>
    {
        policy.PermitLimit = 2;
        policy.QueueLimit = 4; // optional; recommend 0 for security-sensitive ops
    });
});
```

Host pipeline (host-owned):

```csharp
app.UseAuthentication(); // required before UseRateLimiter if using AuthenticatedUserId partitions
app.UseRateLimiter();
app.UseAuthorization();
```

Endpoint application (native ASP.NET Core):

```csharp
app.MapPost("/login", ...).RequireRateLimiting("Login");

// or
[EnableRateLimiting("Login")]
```

Policy names are **exactly** the strings the host registered — no `Permixa.` / `Permixa.` prefix rewrite.

## Algorithms (guidance, not mandates)

| Algorithm | Generally useful for |
|-----------|----------------------|
| **Fixed Window** | Simple quotas and public endpoint limits |
| **Sliding Window** | Sensitive repeated traffic where fixed-window boundary bursts are undesirable |
| **Token Bucket** | Legitimate bursts with gradual replenishment |
| **Concurrency** | Expensive/long-running work where simultaneous execution matters |

Hosts choose freely (e.g. Login may be Sliding Window or Token Bucket).

## Configuration surface

Type-safe builders (invalid algorithm/property mixes are structurally avoided):

| Method | Options type | Key parameters |
|--------|--------------|----------------|
| `AddFixedWindow` | `FixedWindowRateLimitPolicyOptions` | `PermitLimit`, `Window`, `QueueLimit`, `Partition` |
| `AddSlidingWindow` | `SlidingWindowRateLimitPolicyOptions` | `PermitLimit`, `Window`, `SegmentsPerWindow`, `QueueLimit`, `Partition` |
| `AddTokenBucket` | `TokenBucketRateLimitPolicyOptions` | `TokenLimit`, `TokensPerPeriod`, `ReplenishmentPeriod`, `QueueLimit`, `AutoReplenishment`, `Partition` |
| `AddConcurrency` | `ConcurrencyRateLimitPolicyOptions` | `PermitLimit`, `QueueLimit`, `Partition` |

Startup validation fails fast for empty/duplicate policy names and invalid numeric/time values.

Duplicate names throw — last registration does **not** silently win.

## Partitioning

Built-in kinds (`PermixaRateLimitPartitionKind`):

| Kind | Key |
|------|-----|
| `RemoteIp` (default) | `HttpContext.Connection.RemoteIpAddress` as resolved by ASP.NET Core |
| `Global` | Single shared partition for the policy |
| `AuthenticatedUserId` | Normalized `sub` / NameIdentifier; falls back to RemoteIp when anonymous |

Security rules:

- Never use raw secrets/tokens/email/username/OTP as partition keys
- Do not parse `X-Forwarded-For` / `Forwarded` / `X-Real-IP` inside Permixa — hosts must configure trusted forwarding so `RemoteIpAddress` is correct
- No database lookups for partitioning
- Custom partition delegates are **not** exposed; hosts needing custom keys use ASP.NET Core `AddRateLimiter` directly

## Queue behavior

`QueueLimit` is configurable (default **0**). Documentation recommendation: keep `0` for security-sensitive endpoints so excess requests fail immediately with HTTP 429. Bounded queues may be appropriate for expensive work that can wait briefly.

## HTTP 429

On rejection:

- Status `429 Too Many Requests`
- Optional `Retry-After` when limiter metadata provides it
- Minimal ProblemDetails-shaped JSON with code `RateLimiting.TooManyRequests`
- No account existence, lockout, MFA, challenge, or token state

## Process-local limitation

Counters are **per application instance**. Multi-node shared quotas require edge/gateway/WAF solutions. Redis authorization cache is **not** reused for rate-limit state.

## Middleware order

- Anonymous / `RemoteIp` / `Global`: `UseRateLimiter` after routing setup; authentication not required for partitioning
- `AuthenticatedUserId`: place `UseAuthentication` **before** `UseRateLimiter` so the principal is available

Permixa never secretly inserts middleware.

## Coexistence with external gateways

Hosts may combine Permixa policies with Cloudflare, Azure Front Door, NGINX, Kubernetes ingress, or API gateway limits. Those operate outside the process.

## Related

- [ADR-0013](../decisions/ADR-0013-rate-limiting-configuration-layer.md)
- [SECURITY-MODEL.md](../SECURITY-MODEL.md)
- [phases/PHASE-I-0-RATE-LIMITING.md](../phases/PHASE-I-0-RATE-LIMITING.md)
