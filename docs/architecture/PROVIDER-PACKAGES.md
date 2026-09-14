# Optional provider packages

Permixa keeps **optional third-party SDKs** out of the core install graph when a clean boundary exists (ADR-0016).

## Core entry package

```bash
dotnet add package Permixa.AspNetCore
```

Pulls: Domain, Application, Infrastructure (+ Identity / EF SQL Server / JWT foundations).

Does **not** pull: `StackExchange.Redis`, `Resend`.

## Optional providers

| PackageId | Capability | External SDK | Registration |
|-----------|------------|--------------|--------------|
| `Permixa.Caching.Redis` | Authorization snapshot cache | StackExchange.Redis | `AddPermixaRedisAuthorizationCache` |
| `Permixa.Email.Resend` | Email transport | Resend | `AddPermixaResendEmail` |

### Redis

```csharp
services.AddPermixaInfrastructure(...);
services.AddPermixaRedisAuthorizationCache(o =>
{
    o.ConnectionString = "...";
    // KeyPrefix default: permixa:authz:
});
```

Without the Redis package, Infrastructure registers `MemoryPermissionCache`.

SQL remains the authorization source of truth; Redis is disposable cache only.

### Email

Provider-neutral pipeline (Infrastructure):

```csharp
services.AddPermixaEmailDelivery(o =>
{
    o.FromEmail = "...";
    o.FromName = "...";
    o.Branding.ApplicationName = "...";
    o.EmailConfirmationUrlTemplate = "https://.../{challengeId}/.../{token}";
    o.PasswordResetUrlTemplate = "https://.../{challengeId}/.../{token}";
});
```

Transport (Resend provider **or** host `IEmailSender`):

```csharp
services.AddPermixaResendEmail(o => o.ApiKey = "...");
// or
services.AddScoped<IEmailSender, MySender>();
```

Embedded HTML/TXT templates remain in Infrastructure. Idempotency keys for verification (`permixa-verification/{challengeId}`) are created by `EmailVerificationDispatcher`; Resend passes the key to the Resend API.

## What is not split

SQL Server, EF Core, ASP.NET Core Identity, and JWT remain core Infrastructure/AspNetCore concerns by current design.

## Deferred (not Phase I-3)

Moving `IEmailSender` from Infrastructure to Application; unified `AddPermixa()` facade.
