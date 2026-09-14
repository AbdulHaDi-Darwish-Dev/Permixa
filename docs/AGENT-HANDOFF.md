# Permixa Agent Handoff

> **Read this first.** Concise operational context for any new AI agent or developer.
> Deeper detail lives in linked documents — do not treat this file as the full architecture manual.

## Project Identity

**Permixa** is a reusable **Identity & Access Management (IAM)** framework for ASP.NET Core (.NET 8).

It builds **on top of ASP.NET Core Identity** and adds permissions, overrides, role hierarchy, JWT/refresh sessions, verification, email delivery, MFA login orchestration, IAM audit, and Rate Limiting.

**Previous internal name:** Foundation (Phase I-1 / ADR-0014). Do not reintroduce Foundation product branding.

Solution: `Permixa.slnx`  
Primary NuGet package: `Permixa.AspNetCore` @ `0.1.0-preview.1` (local only)  
Optional providers: `Permixa.Caching.Redis`, `Permixa.Email.Resend`

## Current Status

| Item | State |
|------|--------|
| Last completed phase | **Phase I-3 — Optional Redis & Resend provider package split** |
| Current work | None open; await instruction |
| Next approved implementation | **None** — wait for explicit instruction |
| Packaging | **Locally packable**; **not publicly published** |
| Production HTTP controllers | **None** in library projects |
| Sample / disposable consumers | Deleted after validation (not product) |

## Latest Verified Baseline

Verified in-repo on **2026-09-14** (after Phase I-3):

```text
Domain                36
Application          224
Infrastructure       171
AspNetCore            52
Caching.Redis         17
Email.Resend           5
Integration           51
Total                556 / 556

Build: 0 errors, 0 warnings
Skipped: 0
```

## Completed Phases (summary)

Early numbered phases → letter phases **A**–**H**; **I-0** Rate Limiting; **I-1** Permixa branding; **I-2** local NuGet packaging; **I-3** optional Redis/Resend provider packages.

See [CHANGELOG-PHASES.md](CHANGELOG-PHASES.md) and [phases/](phases/).

## Current / Next Approved Work

- **Now:** No open implementation phase. Provider split done (ADR-0016). Local packaging includes six packages.
- **Next:** Owner decisions for public preview (license, repository URL, SourceLink, README/icon) — then explicit publish approval.
- Do **not** `nuget push` / create tags / templates without approval.

## Critical Architecture Decisions (selected)

| Decision | Status |
|----------|--------|
| Identity owns Users/Roles/passwords | Implemented |
| Permissions / RoleLevel not in JWT | Implemented |
| Permission precedence + default deny | Implemented |
| Redis = disposable authz cache; SQL = source of truth | Implemented |
| Rate Limiting = opt-in AspNetCore config layer | ADR-0013 |
| Product identity = Permixa | ADR-0014 |
| PackageIds `Permixa.*`; primary `Permixa.AspNetCore`; local preview `0.1.0-preview.1`; license deferred | ADR-0015 |
| Optional Redis/Resend provider packages; core does not pull those SDKs | ADR-0016 |

## Permanent Agent Rules

1. Implementation agent — STOP on unapproved architecture/security/public-API/license/publish decisions.
2. Do not invent historical rationale.
3. Set-based DB access (`.cursor/rules/database-access.mdc`).
4. Update `/docs` after non-trivial work (`.cursor/rules/project-documentation.mdc`).
5. Do not reintroduce Foundation branding or publish packages without approval.

## Deferred Scope

See [DEFERRED-SCOPE.md](DEFERRED-SCOPE.md). Highlights: public publish, license, SourceLink, templates, SMS/passkeys, distributed rate limiting, unified `AddPermixa()` facade, moving `IEmailSender` to Application.

## Required Reading

1. This file  
2. [CURRENT-STATE.md](CURRENT-STATE.md)  
3. [SECURITY-MODEL.md](SECURITY-MODEL.md)  
4. [ARCHITECTURE.md](ARCHITECTURE.md)  
5. [architecture/PROVIDER-PACKAGES.md](architecture/PROVIDER-PACKAGES.md)  
6. [decisions/README.md](decisions/README.md)  
7. Relevant [architecture/](architecture/) + [phases/](phases/) docs  

## Last Documentation Update

**2026-09-14** — Phase I-3: optional Redis/Resend provider packages; core AspNetCore install no longer pulls those SDKs.
