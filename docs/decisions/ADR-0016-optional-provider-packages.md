# ADR-0016 — Optional Redis and Resend provider packages

- **Status:** Accepted
- **Date context:** Phase I-3 (2026-09-14)

## Context

Phase I-2 showed that installing core `Permixa.AspNetCore` always transitively installed `StackExchange.Redis` and `Resend`, even when hosts used neither `AddPermixaRedisAuthorizationCache` nor Resend email delivery.

Product principle: **install only what you use** where a clean provider boundary exists.

## Decision

1. Split optional third-party SDKs into dedicated packages:
   - `Permixa.Caching.Redis` — Redis authorization-cache provider (`StackExchange.Redis`)
   - `Permixa.Email.Resend` — Resend email transport (`Resend`)
2. Core packages (`Domain`, `Application`, `Infrastructure`, `AspNetCore`) must **not** PackageReference Redis or Resend.
3. Provider-neutral email pipeline stays in Infrastructure (`AddPermixaEmailDelivery`, templates, dispatcher, `IEmailSender`).
4. Redis default remains in-process `MemoryPermissionCache` in Infrastructure; Redis replaces `IPermissionCache` when the Redis package is registered.
5. This ADR applies **only** to Redis and Resend as currently optional integrations.
6. Explicit non-splits (remain core by design for now): SQL Server / EF Core, ASP.NET Core Identity, JWT.

## Consequences

- Hosts that only install `Permixa.AspNetCore` do not get Redis or Resend SDKs.
- Hosts opt in with additional PackageReferences and registration APIs on the provider packages.
- Pre-public: no obsolete shims for APIs moved out of Infrastructure (no public release yet).

## Source/evidence note

Phase I-3 owner-approved PackageIds and split plan; validated via local feed + disposable PackageReference consumers A–D (deleted after validation).
