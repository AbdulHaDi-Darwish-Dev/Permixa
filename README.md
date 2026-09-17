# Permixa

**Identity & Access Management for ASP.NET Core**

Permixa is a reusable security and Identity & Access Management framework for ASP.NET Core that provides authentication, authorization, fine-grained RBAC, JWT and refresh-token sessions, verification, MFA, authorization caching, Rate Limiting, account security, and IAM auditing — without rebuilding the same infrastructure for every application.

> **Status: Preview (`0.1.0-preview.2` release candidate)**  
> Permixa is currently **pre-1.0**. Public APIs and package structure may evolve before the first stable release. Prefer the preview for evaluation and early integration; pin versions and review release notes before upgrading. Framework packages `0.1.0-preview.1` remain on nuget.org until `0.1.0-preview.2` is published.

[![CI](https://github.com/AbdulHaDi-Darwish-Dev/Permixa/actions/workflows/ci.yml/badge.svg)](https://github.com/AbdulHaDi-Darwish-Dev/Permixa/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download)

---

## Why Permixa?

ASP.NET Core Identity gives you solid identity primitives (users, roles, passwords, lockout, token providers). Real applications still repeatedly rebuild the surrounding IAM surface:

| Concern | What teams rebuild |
|---------|--------------------|
| Tokens | RS256 JWT issuance, validation, refresh rotation |
| Sessions | Refresh families, replay containment, logout/revocation |
| Authorization | Permissions, RBAC, overrides, role hierarchy |
| Verification | Email confirmation, OTP, password reset, email change |
| MFA | TOTP login challenge, recovery codes |
| Operations | Authorization caching, Rate Limiting, security audit |

**Permixa packages these concerns into a coherent, host-owned framework** on top of Identity and EF Core — you keep your ASP.NET Core app; Permixa supplies the IAM building blocks.

---

## Features

### Authentication & sessions

- ASP.NET Core Identity integration (`ApplicationUser` / `ApplicationRole`)
- **RS256** JWT access tokens (external RSA key material — you own the keys)
- Refresh-token **rotation** with **FamilyId** session model
- Refresh **replay containment** (compromised refresh invalidates the family)
- Session listing and revocation
- Logout
- Account **lockout** and **disabled-user** protection

Permissions and role levels are **not** embedded in JWTs (authorization is resolved server-side).

### Authorization

- Fine-grained **permissions**
- **RBAC** with role grants
- User **Allow / Deny** overrides
- **Role hierarchy** (`RoleLevel`) for who-may-manage-whom
- Effective permission resolution
- Dynamic ASP.NET Core permission policies (`RequirePermission`)

**Effective permission precedence:**

```text
User Deny  >  User Allow  >  Role Grant  >  Default Deny
```

`RoleLevel` controls hierarchy/administration authority; it does **not** replace the permission model.

### Authorization caching

- Process-local **`MemoryPermissionCache`** by default
- Versioned snapshots (`AuthorizationVersion`, `RbacVersion`)
- **SQL Server remains the source of truth**
- Optional **Redis** provider for distributed snapshot caching (not Rate Limiting)

### Verification & OTP

- Email confirmation (OTP and URL-token flows)
- Challenge cooldown, attempt limits, and expiry
- Password recovery
- Email change (pending-email model)

### MFA

- TOTP authenticator apps (Identity-backed)
- Dedicated MFA login challenge after password success
- Recovery codes
- Tokens issued only after successful MFA completion

### Security auditing

- IAM mutation audit events
- SQL-backed default sink (replaceable)
- Actor / target tracking
- Secrets excluded from audit metadata and logs

### Rate Limiting

Opt-in configuration over **ASP.NET Core native** Rate Limiting:

- Fixed Window · Sliding Window · Token Bucket · Concurrency
- Host-defined policy names
- Partitions: Remote IP, Global, Authenticated user id
- **No global limiter** — endpoints opt in explicitly
- Process-local counters (not Redis-backed quotas)

### Optional providers

| Package | Purpose |
|---------|---------|
| [`Permixa.Caching.Redis`](Permixa.Caching.Redis/README.md) | Redis authorization snapshot cache |
| [`Permixa.Email.Resend`](Permixa.Email.Resend/README.md) | Resend email transport |

Core install does **not** pull Redis or Resend SDKs.

---

## Requirements

- **.NET 8**
- **SQL Server** (currently the only supported database provider)
- ASP.NET Core host (JWT bearer / permission endpoints as needed)

Optional:

- Redis (authorization cache only)
- Resend (or any custom `IEmailSender`)

---

## Installation

Most applications should start with the primary package:

```bash
dotnet add package Permixa.AspNetCore --prerelease
```

Optional providers:

```bash
dotnet add package Permixa.Caching.Redis --prerelease
dotnet add package Permixa.Email.Resend --prerelease
```

> You normally do **not** need to reference `Permixa.Domain`, `Permixa.Application`, or `Permixa.Infrastructure` directly — they arrive transitively with `Permixa.AspNetCore`.

### Optional application template

Prefer a full Clean Architecture starter instead of wiring packages by hand:

```bash
dotnet new install Permixa.Templates --version 0.1.0-preview.2
dotnet new permixa-app -n MyApp
```

Templates are **optional**. Existing projects can keep using `Permixa.AspNetCore` directly. See [Permixa.Templates/README.md](Permixa.Templates/README.md).

---

## Quick start

```csharp
using Permixa.AspNetCore.Authentication;
using Permixa.AspNetCore.Authorization;
using Permixa.AspNetCore.ProblemDetails;
using Permixa.AspNetCore.RateLimiting;
using Permixa.Infrastructure;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPermixaInfrastructure(o =>
{
    o.ConnectionString = builder.Configuration.GetConnectionString("Permixa")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:Permixa");
    o.Bootstrap.Enabled = builder.Configuration.GetValue("Permixa:Bootstrap:Enabled", false);
    o.Bootstrap.OwnerEmail = builder.Configuration["Permixa:Bootstrap:OwnerEmail"];
    o.Bootstrap.OwnerUserName = builder.Configuration["Permixa:Bootstrap:OwnerUserName"];
    o.Bootstrap.OwnerPassword = builder.Configuration["Permixa:Bootstrap:OwnerPassword"];
});

builder.Services.AddPermixaAuthentication(o =>
{
    o.Authentication.RequireConfirmedEmail = true;
    o.Jwt.Issuer = builder.Configuration["Permixa:Jwt:Issuer"]!;
    o.Jwt.Audience = builder.Configuration["Permixa:Jwt:Audience"]!;
    o.Jwt.PrivateKeyPem = builder.Configuration["Permixa:Jwt:PrivateKeyPem"]!;
});

builder.Services.AddPermixaAuthorization();
builder.Services.AddPermixaVerification(); // optional — verification use cases

builder.Services.AddPermixaJwtBearer(o =>
{
    o.Issuer = builder.Configuration["Permixa:Jwt:Issuer"]!;
    o.Audience = builder.Configuration["Permixa:Jwt:Audience"]!;
    o.PublicKeyPem = builder.Configuration["Permixa:Jwt:PublicKeyPem"]!;
});

builder.Services.AddPermixaPermissionAuthorization();
builder.Services.AddPermixaProblemDetails();

builder.Services.AddPermixaRateLimiting(o =>
{
    o.AddSlidingWindow("Login", p =>
    {
        p.PermitLimit = 20;
        p.Window = TimeSpan.FromMinutes(1);
        p.Partition = PermixaRateLimitPartitionKind.RemoteIp;
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseRateLimiter(); // only if AddPermixaRateLimiting was called
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    // Explicit bootstrap — not automatic on every request
    await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>()
        .BootstrapAsync();
}

app.MapGet("/admin/permissions", () => Results.Ok())
    .RequireAuthorization()
    .RequirePermission("Iam.Permissions.Read")
    .RequireRateLimiting("Login");

app.Run();
```

Store connection strings, bootstrap credentials, and RSA PEMs in **user secrets / environment / a secret manager** — never commit them.

### Middleware order (verified)

```text
UseExceptionHandler
→ UseAuthentication
→ UseRateLimiter   (when Rate Limiting is enabled; place after authentication if using AuthenticatedUserId partitions)
→ UseAuthorization
```

### Migrations & bootstrap

The **host** owns startup:

1. `ApplicationDbContext.Database.MigrateAsync()` (or your preferred migration strategy)
2. `IPermixaBootstrapper.BootstrapAsync()` when bootstrap is enabled

Bootstrap is **idempotent** and creates the Owner / IAM seed when configured. Disable bootstrap and remove secrets after first-time setup. Newly created Owners are created with `EmailConfirmed = true` so they can authenticate when `RequireConfirmedEmail` is enabled.

---

## Optional Redis authorization cache

```bash
dotnet add package Permixa.Caching.Redis --prerelease
```

```csharp
using Permixa.Caching.Redis;

services.AddPermixaRedisAuthorizationCache(o =>
{
    o.ConnectionString = builder.Configuration.GetConnectionString("Redis");
    // KeyPrefix default: permixa:authz:
});
```

- Replaces the default in-process memory cache registration
- Redis holds **disposable authorization snapshots** only
- **SQL remains authoritative**; Redis failures fail open to SQL rebuild paths

This is **not** a distributed Rate Limiting store.

---

## Optional email delivery (Resend or custom)

Provider-neutral pipeline (templates, links, dispatcher) lives in core Infrastructure:

```csharp
services.AddPermixaEmailDelivery(o =>
{
    o.FromEmail = "noreply@example.com";
    o.FromName = "My App";
    o.Branding.ApplicationName = "My App";
    o.EmailConfirmationUrlTemplate =
        "https://app.example.com/verify?challengeId={challengeId}&token={token}";
    o.PasswordResetUrlTemplate =
        "https://app.example.com/reset?challengeId={challengeId}&token={token}";
});
```

Resend transport:

```bash
dotnet add package Permixa.Email.Resend --prerelease
```

```csharp
using Permixa.Email.Resend;

services.AddPermixaResendEmail(o =>
{
    o.ApiKey = builder.Configuration["Permixa:Resend:ApiKey"]!;
});
```

Or register your own `IEmailSender` (SMTP, SES, SendGrid, test doubles) — **Resend is not required**.

---

## Rate Limiting example

```csharp
services.AddPermixaRateLimiting(options =>
{
    options.AddSlidingWindow("Login", o =>
    {
        o.PermitLimit = 30;
        o.Window = TimeSpan.FromMinutes(1);
        o.Partition = PermixaRateLimitPartitionKind.RemoteIp;
    });

    options.AddTokenBucket("Otp", o =>
    {
        o.TokenLimit = 10;
        o.TokensPerPeriod = 10;
        o.ReplenishmentPeriod = TimeSpan.FromMinutes(1);
        o.Partition = PermixaRateLimitPartitionKind.RemoteIp;
    });
});

// Endpoint
.RequireRateLimiting("Login");
```

Permixa does **not** globally rate-limit your host. You choose policy names and where they apply.

---

## Package map

| Package | Role |
|---------|------|
| **Permixa.AspNetCore** | Primary entry — JWT bearer, permissions, ProblemDetails, Rate Limiting |
| Permixa.Infrastructure | Identity, EF Core/SQL Server, email delivery pipeline, bootstrap |
| Permixa.Application | Use cases & abstractions |
| Permixa.Domain | Domain models & security primitives |
| Permixa.Caching.Redis | Optional Redis authz cache |
| Permixa.Email.Resend | Optional Resend transport |

---

## Security

Permixa documents concrete security behaviors (JWT model, refresh families, permission precedence, MFA proof handling, audit secret exclusion, Rate Limiting partitions). It does **not** claim to be a complete identity provider or “unbreakable” security.

See **[SECURITY.md](SECURITY.md)** for vulnerability reporting and **[docs/SECURITY-MODEL.md](docs/SECURITY-MODEL.md)** for invariants.

---

## Known limitations (preview)

- **SQL Server only** (no SQLite / PostgreSQL / MySQL provider)
- **.NET 8 only**
- Rate Limiting is **process-local**
- Redis provider is **authorization cache only** (not distributed rate limits)
- No OAuth/OIDC **authorization server**, SSO/SAML, passkeys/WebAuthn, or SMS MFA
- No multi-tenancy product model
- No access-token blacklist (short-lived JWT + refresh/session revoke)
- **No production HTTP controllers** shipped — hosts own APIs
- **Pre-1.0** — public APIs may change

---

## Documentation

| Topic | Document |
|-------|----------|
| Security model | [docs/SECURITY-MODEL.md](docs/SECURITY-MODEL.md) |
| Architecture | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) |
| Provider packages | [docs/architecture/PROVIDER-PACKAGES.md](docs/architecture/PROVIDER-PACKAGES.md) |
| Rate Limiting | [docs/architecture/RATE-LIMITING.md](docs/architecture/RATE-LIMITING.md) |
| Changelog | [CHANGELOG.md](CHANGELOG.md) |
| Contributing | [CONTRIBUTING.md](CONTRIBUTING.md) |

---

## License

Licensed under the **Apache License 2.0**. See [LICENSE](LICENSE).

Copyright © 2026 Abdulhadi Darwish

> **Permixa Core** is open source. Future **Permixa Enterprise / Cloud** offerings (if any) may be separate proprietary products and are **not** part of this package license.

---

## Support

- Issues: [GitHub Issues](https://github.com/AbdulHaDi-Darwish-Dev/Permixa/issues)
- Security: [SECURITY.md](SECURITY.md)
