# Permixa Agent Handoff

> **Read this first.** Concise operational context for any new AI agent or developer.
> Deeper detail lives in linked documents — do not treat this file as the full architecture manual.

## Project Identity

**Permixa** is a reusable **Identity & Access Management (IAM)** framework for ASP.NET Core (.NET 8).

It builds **on top of ASP.NET Core Identity** and adds permissions, overrides, role hierarchy, JWT/refresh sessions, verification, email delivery, MFA login orchestration, IAM audit, and Rate Limiting.

**Previous internal name:** Foundation (Phase I-1 / ADR-0014). Do not reintroduce Foundation product branding.

Solution: `Permixa.slnx`  
Primary NuGet package: `Permixa.AspNetCore` @ `0.1.0-preview.1`  
Optional providers: `Permixa.Caching.Redis`, `Permixa.Email.Resend`  
Canonical repo: https://github.com/AbdulHaDi-Darwish-Dev/Permixa  
License: **Apache-2.0**

## Current Status

| Item | State |
|------|--------|
| Last completed phase | **Phase I-5 — Public Preview Preparation & Final Dry Run** |
| Current work | None open; await **explicit publish approval** |
| Next approved implementation | **None** — NuGet publish only after owner approval |
| Packaging | **Public-preview ready pending explicit publish approval** |
| Production HTTP controllers | **None** in library projects |
| Sample / disposable consumers | Deleted after validation (not product) |

## Latest Verified Baseline

Verified in-repo on **2026-09-14** (after Phase I-5):

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

Early numbered phases → **A**–**H**; **I-0**–**I-5** (Rate Limiting → branding → packaging → providers → readiness → public preview prep).

See [CHANGELOG-PHASES.md](CHANGELOG-PHASES.md) and [phases/](phases/).

## Current / Next Approved Work

- **Now:** Preview preparation complete (ADR-0017). **Do not** `nuget push` / tag / GitHub Release without explicit approval.
- **Next:** Owner-approved publish of `0.1.0-preview.1` to NuGet.org (and optional prefix reservation after).
- Push local `main` to GitHub when owner directs.

## Critical Architecture Decisions (selected)

| Decision | Status |
|----------|--------|
| Identity owns Users/Roles/passwords | Implemented |
| Permissions / RoleLevel not in JWT | Implemented |
| Redis = disposable authz cache; SQL = source of truth | Implemented |
| Optional Redis/Resend provider packages | ADR-0016 |
| Apache-2.0 core; canonical GitHub repo; SourceLink; validation CI | ADR-0017 |

## Permanent Agent Rules

1. STOP on unapproved architecture/security/public-API/license/**publish** decisions.
2. Do not invent historical rationale.
3. Set-based DB access (`.cursor/rules/database-access.mdc`).
4. Update `/docs` after non-trivial work.
5. Do not reintroduce Foundation branding or publish packages without approval.

## Deferred Scope

See [DEFERRED-SCOPE.md](DEFERRED-SCOPE.md). Highlights: package icon, `Permixa.*` prefix reservation, CLA/DCO, templates, SMS/passkeys, `AddPermixa()` facade.

## Required Reading

1. This file  
2. [CURRENT-STATE.md](CURRENT-STATE.md)  
3. [SECURITY-MODEL.md](SECURITY-MODEL.md)  
4. Root [README.md](../README.md) (public)  
5. [decisions/README.md](decisions/README.md)  

## Last Documentation Update

**2026-09-14** — Phase I-5: public preview preparation complete; not published.
