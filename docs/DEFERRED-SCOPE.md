# Deferred scope

Intentionally deferred or paused work. Not “forgotten” unless marked otherwise.

## Packaging and distribution

| Item | Status |
|------|--------|
| NuGet framework packages `0.1.0-preview.1` | **Published** on nuget.org (see GitHub Release `v0.1.0-preview.1`) |
| Unified `0.1.0-preview.2` (seven packages + icon) | **Release candidate** — ready for owner publish approval; **not published** |
| Preview publish train | **Lockstep** — only `.github/workflows/publish-nuget.yml` (Templates-only workflow removed in R2.1) |
| .NET project templates (`Permixa.Templates` / `permixa-app`) | Included in preview.2 RC; **not published** yet |
| Package icon | **Resolved** for preview.2 — `assets/permixa-icon.png` |
| Permanent product license | **Resolved** — Apache-2.0 for Permixa Core (ADR-0017) |
| RepositoryUrl / SourceLink / package README | **Resolved** for preview (ADR-0017); icon deferred |
| Public API trim / analyzer package | Not implemented |
| Move `IEmailSender` to Application | Deferred |
| Unified `AddPermixa()` registration facade | Future DX recommendation only |
| NuGet prefix reservation (`Permixa.*`) | Deferred / verify after publishes |
| CLA / DCO for external PRs | Deferred — CONTRIBUTING limits PR acceptance for now |
| Package icon | Deferred (non-blocking for preview) — **superseded by R2** (`assets/permixa-icon.png`) |
| Cursor `.cursor/rules` in generated apps | Deferred (v1 uses `AGENTS.md`) |

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

- Sample host exposing audit read + IAM admin HTTP (beyond template reference endpoints)
- Focused authentication audit with retention policy
- Optional convenience IAM rate-limit presets

Do not treat recommendations as project decisions.
