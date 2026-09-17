# Permixa Agent Handoff

> **Read this first.** Concise operational context for any new AI agent or developer.
> Deeper detail lives in linked documents — do not treat this file as the full architecture manual.

## Project Identity

**Permixa** is a reusable **Identity & Access Management (IAM)** framework for ASP.NET Core (.NET 8).

It builds **on top of ASP.NET Core Identity** and adds permissions, overrides, role hierarchy, JWT/refresh sessions, verification, email delivery, MFA login orchestration, IAM audit, and Rate Limiting.

**Previous internal name:** Foundation (Phase I-1 / ADR-0014). Do not reintroduce Foundation product branding.

Solution: `Permixa.slnx`  
Primary NuGet package: `Permixa.AspNetCore` @ `0.1.0-preview.2` (RC; preview.1 still on nuget.org until publish)  
Optional providers: `Permixa.Caching.Redis`, `Permixa.Email.Resend`  
Templates: `Permixa.Templates` / `permixa-app` @ `0.1.0-preview.2` (RC; **not published yet**)  
Package icon: `assets/permixa-icon.png`  
Canonical repo: https://github.com/AbdulHaDi-Darwish-Dev/Permixa  
License: **Apache-2.0**

## Current Status

| Item | State |
|------|--------|
| Last completed phase | **Phase R2.1 — Preview.2 release lockdown** |
| Current work | Await owner publish approval for `0.1.0-preview.2` |
| Preview publish | Lockstep only via `publish-nuget.yml` (seven packages) |
| Next approved implementation | **None** until explicitly directed |
| nuget.org today | Framework `0.1.0-preview.1` published; Templates / preview.2 **not** published |
| Production HTTP controllers | **None** in library projects |

## Latest Verified Baseline

- Framework: **556 / 556** passed (0 skipped) — R2 fresh Release run
- Templates (local RC feed): base 8, `--redis` 9, `--resend` 8, `--redis --resend` 9; rename builds OK
- See [CURRENT-STATE.md](CURRENT-STATE.md)

## Critical Architecture Decisions (selected)

| Decision | Status |
|----------|--------|
| Identity owns Users/Roles/passwords | Implemented |
| Permissions / RoleLevel not in JWT | Implemented |
| Redis = disposable authz cache; SQL = source of truth | Implemented |
| Optional Redis/Resend provider packages | ADR-0016 |
| Apache-2.0 core; canonical GitHub repo; SourceLink; validation CI | ADR-0017 |
| Consumer template: CA host + two DbContexts + `--redis`/`--resend` | Phase T1/T2 |
| Shared NuGet icon | R2 (`assets/permixa-icon.png`) |

## Permanent Agent Rules

1. STOP on unapproved architecture/security/public-API/license/**publish** decisions.
2. Do not invent historical rationale.
3. Set-based DB access (`.cursor/rules/database-access.mdc`).
4. Update `/docs` after non-trivial work.
5. Do not reintroduce Foundation branding or publish packages without approval.

## Deferred Scope

See [DEFERRED-SCOPE.md](DEFERRED-SCOPE.md).

## Required Reading

1. This file  
2. [CURRENT-STATE.md](CURRENT-STATE.md)  
3. [SECURITY-MODEL.md](SECURITY-MODEL.md)  
4. Root [README.md](../README.md) (public)  
5. [decisions/README.md](decisions/README.md)  

## Last Documentation Update

**2026-09-17** — Phase R2.1: `0.1.0-preview.2` lockdown (removed Templates-only publish workflow; lockstep `publish-nuget.yml` only). **Not published.**
