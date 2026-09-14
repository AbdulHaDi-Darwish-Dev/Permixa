# Phase I-3 — Optional Redis & Resend provider package split

## Goal

Make Redis and Resend optional at **both** runtime and NuGet package levels (“install only what you use”), without redesigning authorization-cache or email-verification semantics.

## Outcome

| Item | Result |
|------|--------|
| New packages | `Permixa.Caching.Redis`, `Permixa.Email.Resend` @ `0.1.0-preview.1` |
| Core | `Permixa.AspNetCore` / `Infrastructure` **no longer** depend on `StackExchange.Redis` or `Resend` |
| Neutral email API | `AddPermixaEmailDelivery` + `PermixaEmailDeliveryOptions` in Infrastructure |
| Resend API | `AddPermixaResendEmail` + `PermixaResendEmailOptions` (ApiKey) in `Permixa.Email.Resend` |
| Redis API | `AddPermixaRedisAuthorizationCache` in `Permixa.Caching.Redis` |
| Default cache | `MemoryPermissionCache` remains in Infrastructure |
| Consumers A–D | PackageReference validated then **deleted** |
| ADR | [ADR-0016](../decisions/ADR-0016-optional-provider-packages.md) |

## Dependency graph (after)

```text
Permixa.Domain ← Permixa.Application ← Permixa.Infrastructure ← Permixa.AspNetCore

Permixa.Application ← Permixa.Caching.Redis → StackExchange.Redis
Permixa.Infrastructure ← Permixa.Email.Resend → Resend
```

## Verified after phase

See [CURRENT-STATE.md](../CURRENT-STATE.md).
