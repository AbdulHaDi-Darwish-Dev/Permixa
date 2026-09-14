# Deferred scope

Intentionally deferred or paused work. Not “forgotten” unless marked otherwise.

## Packaging and distribution

| Item | Status |
|------|--------|
| NuGet packaging | **Locally packable** (`0.1.0-preview.1`); **not publicly published** (ADR-0015) |
| Permanent product license | **Resolved** — Apache-2.0 for Permixa Core (ADR-0017) |
| RepositoryUrl / SourceLink / package README | **Resolved** for preview (ADR-0017); icon deferred |
| Public API trim / analyzer package | Not implemented |
| .NET project templates | Deferred / future direction |
| Sample / template host HTTP API | Deferred (libraries have no production controllers). Disposable consumers deleted after validation. |
| Bootstrap Owner vs `RequireConfirmedEmail` | **Resolved** (ADR-0012) — new Owner `EmailConfirmed=true` |
| Provider package split (Redis / Resend) | **Done** — ADR-0016 / Phase I-3 |
| Move `IEmailSender` to Application | Deferred (kept in Infrastructure for Phase I-3) |
| Unified `AddPermixa()` registration facade | Future DX recommendation only |
| NuGet prefix reservation (`Permixa.*`) | Deferred until after first public publication |
| CLA / DCO for external PRs | Deferred — CONTRIBUTING limits PR acceptance for now |
| Package icon | Deferred (non-blocking for preview) |

## Identity / MFA / verification

| Item | Status |
|------|--------|
| SMS / phone verification | Deferred |
| SMS MFA | Deferred |
| Email OTP as MFA | Deferred (email OTP may exist for verification purposes — not MFA login) |
| Passkeys / WebAuthn | Deferred |
| Remember device / trusted devices | Deferred |
| Device metadata on sessions | Deferred |
| External identity providers (OIDC/social) | Deferred |
| Admin MFA reset expansion | Deferred beyond Phase F scope |

## Audit

| Item | Status |
|------|--------|
| Login/authentication high-volume audit events | Deferred (Phase G) |
| Composite multi-sink / SIEM outbox / Kafka | Deferred |
| Cryptographic audit hash chaining | Deferred |
| Automatic retention/archive jobs | Deferred (host policy) |
| CorrelationId population | Deferred (null in Phase G) |
| Email addresses in audit metadata | Avoided / deferred |

## Platform

| Item | Status |
|------|--------|
| Multi-tenancy | Not part of current Permixa design |
| Access-token blacklist | Explicitly not adopted |
| DeleteUser | Not implemented (audit IDs must remain meaningful if added later) |
| Distributed Redis/SQL rate limiting | Deferred (Phase I-0 is process-local) |
| Custom rate-limit partition-key delegates | Deferred (use native ASP.NET Core APIs) |
| Built-in IAM convenience rate-limit policies | Deferred / not approved for I-0 |

## Recommendations (not approved)

These are **future recommendations** only:

- Sample host exposing audit read + IAM admin HTTP
- Focused authentication audit with retention policy
- Resume packaging after public API review + Permixa→Permixa branding migration
- Optional convenience IAM rate-limit presets (separate from the generic config API)

Do not treat recommendations as project decisions.
