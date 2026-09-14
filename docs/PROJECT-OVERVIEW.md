# Project overview

## Purpose

**Permixa** is a reusable **Identity and Access Management (IAM)** framework for ASP.NET Core applications.

**Verified goal:** give consuming hosts Identity-backed authentication and Permixa-owned authorization, sessions, verification, MFA, and audit without each host reinventing those subsystems.

**Future direction (vision from early project prompts; not all implemented):** NuGet distribution and .NET project templates.

## Stack

- .NET 8 (`net8.0`)
- ASP.NET Core Identity + EF Core SQL Server
- JWT access tokens (RS256) + stateful refresh tokens
- Redis (authorization snapshot cache only)
- Resend (email delivery implementation)
- xUnit + Testcontainers (SQL Server, Redis where applicable)

## Feature status map

Labels: **Implemented** | **Approved but not implemented** | **Deferred** | **Open decision** | **Future recommendation**

| Capability | Status |
|------------|--------|
| Clean Architecture layering (Domain → Application → Infrastructure → AspNetCore) | Implemented |
| ASP.NET Core Identity users/roles/passwords/lockout/2FA primitives | Implemented |
| Permissions, role grants, user overrides, default deny | Implemented |
| Role hierarchy (`RoleLevel`, placement, collision shifting) | Implemented |
| Effective permission resolution + Redis snapshot cache | Implemented |
| JWT RS256 + refresh rotation/families | Implemented |
| Verification challenges (email confirmation / email change, etc.) | Implemented |
| Email delivery abstraction + Resend + embedded templates | Implemented |
| AspNetCore JWT bearer + permission authorization handler | Implemented |
| Role/UserRole/user admin/session/credential use cases | Implemented |
| MFA v1 (TOTP + recovery codes + login challenge) | Implemented |
| IAM audit sink + SQL default + audit read | Implemented |
| Production controllers / sample API | Deferred |
| NuGet packaging / templates | Deferred (packaging paused) |
| SMS / phone verification | Deferred |
| SMS MFA / passkeys / remember device | Deferred |
| External identity providers | Deferred |
| Login authentication audit volume events | Deferred |
| Audit outbox / multi-sink SIEM | Deferred |
| Multi-tenancy | Not in scope historically; not implemented |

## What Permixa is not

- Not a finished packaged NuGet product yet.
- Not a full application host with production HTTP endpoints.
- Not a generic enterprise event store (audit is IAM-focused).
- Not a replacement for ASP.NET Core Identity primitives.

## Related docs

- [AGENT-HANDOFF.md](AGENT-HANDOFF.md)
- [ARCHITECTURE.md](ARCHITECTURE.md)
- [DEFERRED-SCOPE.md](DEFERRED-SCOPE.md)
