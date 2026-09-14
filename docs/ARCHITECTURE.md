# Architecture

## Layering (actual)

```text
Permixa.Domain          (no project refs)
        ↑
Permixa.Application     → Domain
        ↑
Permixa.Infrastructure  → Application
        ↑
Permixa.AspNetCore      → Application + Infrastructure

Optional providers (do not sit on the core install path):

Permixa.Caching.Redis   → Application + StackExchange.Redis
Permixa.Email.Resend    → Infrastructure + Resend
```

Tests:

- `Permixa.Domain.Tests` → Domain  
- `Permixa.Application.Tests` → Application (+ Domain)  
- `Permixa.Infrastructure.Tests` → Infrastructure  
- `Permixa.AspNetCore.Tests` → AspNetCore  
- `Permixa.Caching.Redis.Tests` → Caching.Redis (+ Infrastructure for e2e)  
- `Permixa.Email.Resend.Tests` → Email.Resend  
- `Permixa.IntegrationTests` → AspNetCore + providers as needed  

Solution file: `Permixa.slnx`.

See [architecture/PROVIDER-PACKAGES.md](architecture/PROVIDER-PACKAGES.md) and ADR-0016.

## Dependency rules

| Layer | Owns | Must not |
|-------|------|----------|
| **Domain** | Pure entities/rules: Permission, RolePermission, UserPermissionOverride, AuthorizationState, RefreshToken, VerificationChallenge, MfaLoginChallenge, IamAuditLog | Depend on Identity, EF, ASP.NET, Redis, HTTP |
| **Application** | Use cases, DTOs, abstractions (`I*Repository`, `IIamAuditSink`, Identity *readers/writers* as interfaces), IAM permission catalog | Reference concrete Identity/EF types |
| **Infrastructure** | `ApplicationUser` / `ApplicationRole`, `ApplicationDbContext`, EF configs/migrations, Identity adapters, JWT generator, memory authz cache, provider-neutral email delivery pipeline, SQL audit sink | Leak concrete Identity types upward into Domain/Application contracts; PackageReference optional Redis/Resend SDKs |
| **AspNetCore** | Host integration: JWT bearer options, permission authorization handler, exception/problem-details mapping, result HTTP helpers, Rate Limiting configuration | Own business policy that belongs in Application |
| **Caching.Redis** | Optional Redis `IPermissionCache` | Depend on Infrastructure/AspNetCore |
| **Email.Resend** | Optional Resend `IEmailSender` transport | Depend on AspNetCore |

**Verified historical decision:** ASP.NET Core Identity owns Users/Roles/password primitives. Domain originally had User/Role/UserRole; they were **removed** in the Identity-alignment correction (Phase 1.1). Concrete Identity types live in Infrastructure.

## Concern placement (current)

- **Permission math:** Domain `PermissionAuthorizationResolver`
- **Hierarchy math:** Application `RoleHierarchyRules` / `RolePlacementCalculator`
- **Effective permissions + cache:** Application `EffectivePermissionService` + Infrastructure `MemoryPermissionCache` (default) or optional `Permixa.Caching.Redis`
- **Token issuance/refresh/logout:** Application `AuthenticationTokenService` + Infrastructure JWT generator + refresh repository
- **MFA:** Application use cases + Domain `MfaLoginChallenge` + Identity authenticator APIs in Infrastructure
- **Audit write/read:** Application abstractions + Infrastructure SQL implementations
- **HTTP:** AspNetCore only (no production controllers in libraries)
- **Rate Limiting:** AspNetCore opt-in configuration over native ASP.NET Core Rate Limiting (host-defined policies)

## Configuration entry points

- `AddPermixaInfrastructure(...)` — Infrastructure DI (Identity, EF, JWT options validation, memory authz cache, audit `TryAddScoped` sink/reader, etc.)
- `AddPermixaEmailDelivery(...)` — provider-neutral email templates/links/dispatcher (no transport)
- `AddPermixaRedisAuthorizationCache(...)` — **Permixa.Caching.Redis** only
- `AddPermixaResendEmail(...)` — **Permixa.Email.Resend** only (transport)
- `AddPermixaAuthorization()` — registers authorization/audit use cases (among others)
- AspNetCore extensions for JWT bearer, permission policies, ProblemDetails, and **opt-in** `AddPermixaRateLimiting`
- Rate Limiting is **not** auto-registered by other `AddPermixa*` methods; hosts must call `UseRateLimiter` and apply named policies

Exact method inventories evolve; inspect Infrastructure / provider `DependencyInjection.cs` and AspNetCore extension classes before changing registration.

## Persistence

SQL Server via EF Core. Design-time connection: environment variable `PERMIXA_CONNECTION_STRING` (approved Phase 3 decision; no hardcoded LocalDB credentials in factory). Runtime connection string must be supplied by the host (fail fast if missing).

## What this doc is not

This describes the **repository as it exists**, not an idealized Clean Architecture that differs from the code.
