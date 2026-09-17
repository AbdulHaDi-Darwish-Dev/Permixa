# Changelog

All notable **public** changes to Permixa packages are documented here.

Internal development phase history lives in [`docs/CHANGELOG-PHASES.md`](docs/CHANGELOG-PHASES.md) and is not a substitute for this file.

The format is inspired by [Keep a Changelog](https://keepachangelog.com/). Versions follow SemVer with NuGet prerelease labels.

## [0.1.0-preview.2] — 2026-09-17

Unified preview release candidate (not published until owner approval).

### Added

- `Permixa.Templates` / `permixa-app` — optional Clean Architecture ASP.NET Core application starter
  - Domain / Application / Infrastructure / Api + tests
  - Generated docs, Docker Compose (SQL Server; Redis when selected), CI workflow
  - Template options: `--redis`, `--resend`
  - Development migrate / bootstrap / optional app permission seed
- Shared NuGet package icon (`assets/permixa-icon.png`) on all seven packages

### Changed

- Package version lockstep: all seven packages at `0.1.0-preview.2`
- Generated apps pin Permixa packages to `0.1.0-preview.2`
- Main `publish-nuget.yml` publishes all seven packages (lockstep preview train; sole publication workflow)

### Notes

- No IAM/RBAC/MFA/schema behavioral changes vs `0.1.0-preview.1` framework packages in this release train
- Templates-only publish workflow removed during preview to prevent version skew

## [0.1.0-preview.1] — 2026-09-14

First public preview of **Permixa IAM** for ASP.NET Core.

### Added

- Core packages: `Permixa.Domain`, `Permixa.Application`, `Permixa.Infrastructure`, `Permixa.AspNetCore`
- Optional providers: `Permixa.Caching.Redis`, `Permixa.Email.Resend`
- ASP.NET Core Identity integration with SQL Server / EF Core
- RS256 JWT access tokens and refresh-token rotation (FamilyId sessions)
- RBAC, fine-grained permissions, user Allow/Deny overrides, role hierarchy
- Authorization snapshot caching (memory default; optional Redis)
- Verification / OTP (email confirmation, password recovery, email change)
- TOTP MFA login challenge and recovery codes
- IAM audit sink (SQL default, replaceable)
- Opt-in Rate Limiting configuration layer (Fixed / Sliding / Token Bucket / Concurrency)
- Host integration: JWT bearer, permission policies, ProblemDetails mapping
- Explicit IAM bootstrap (`IPermixaBootstrapper`)

### Security

- Permissions and RoleLevel are not placed in JWTs
- Refresh replay containment via family revocation
- Audit / logging policies exclude secrets (passwords, tokens, OTP, MFA proofs)
- Rate-limit 429 responses avoid leaking account/challenge state

### Known limitations

- Preview / pre-1.0 — public APIs may evolve
- .NET 8 and SQL Server only
- Rate Limiting is process-local
- Redis provider is authorization cache only (not distributed rate limiting)
- No OAuth/OIDC server, SSO/SAML, passkeys, SMS MFA, or multi-tenancy
- No access-token blacklist
- No production HTTP controllers shipped in packages

[0.1.0-preview.2]: https://github.com/AbdulHaDi-Darwish-Dev/Permixa/releases/tag/v0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/AbdulHaDi-Darwish-Dev/Permixa/releases/tag/v0.1.0-preview.1
