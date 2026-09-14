# Architecture decision records

ADRs capture decisions future agents could accidentally reverse.

## Index

| ADR | Title | Status |
|-----|-------|--------|
| [ADR-0001](ADR-0001-aspnet-core-identity.md) | Use ASP.NET Core Identity for users/roles/passwords | Accepted |
| [ADR-0002](ADR-0002-permission-precedence.md) | Permission precedence and default deny | Accepted |
| [ADR-0003](ADR-0003-permissions-not-in-jwt.md) | Permissions and RoleLevel not in JWT | Accepted |
| [ADR-0004](ADR-0004-collision-only-role-levels.md) | Collision-only RoleLevel placement | Accepted |
| [ADR-0005](ADR-0005-authorization-versions-and-redis.md) | AuthorizationVersion + RbacVersion; Redis disposable cache | Accepted |
| [ADR-0006](ADR-0006-refresh-token-families.md) | Refresh-token FamilyId session model | Accepted |
| [ADR-0007](ADR-0007-rs256-jwt-no-blacklist.md) | RS256 JWT; no access-token blacklist | Accepted |
| [ADR-0008](ADR-0008-pending-email.md) | PendingEmail for email change | Accepted |
| [ADR-0009](ADR-0009-login-result-mfa.md) | LoginResult Authenticated vs MfaRequired | Accepted |
| [ADR-0010](ADR-0010-mfa-login-challenge.md) | Dedicated MfaLoginChallenge table | Accepted |
| [ADR-0011](ADR-0011-sql-iam-audit-sink.md) | SQL IAM audit sink (stage-only) | Accepted |
| [ADR-0012](ADR-0012-bootstrap-owner-email-confirmed.md) | Bootstrap Owner EmailConfirmed=true | Accepted |
| [ADR-0013](ADR-0013-rate-limiting-configuration-layer.md) | Opt-in AspNetCore Rate Limiting configuration layer | Accepted |
| [ADR-0014](ADR-0014-foundation-to-permixa-branding.md) | Foundation → Permixa pre-public branding migration | Accepted |
| [ADR-0015](ADR-0015-local-nuget-packaging.md) | Local NuGet packaging for Permixa preview | Accepted |
| [ADR-0016](ADR-0016-optional-provider-packages.md) | Optional Redis / Resend provider packages | Accepted |
| [ADR-0017](ADR-0017-public-preview-packaging.md) | Apache-2.0, canonical repo, SourceLink, validation CI | Accepted |

## Naming note

The product was originally developed under the internal name **Foundation** and renamed to **Permixa** in Phase I-1 before public packaging (ADR-0014). Older ADRs may still mention Foundation historically; current source/API identity is Permixa.

## How to add an ADR

Only when an architecture/security/schema/public-API/concurrency decision is **explicitly approved**. Include evidence note. Never invent alternatives that were not discussed.
