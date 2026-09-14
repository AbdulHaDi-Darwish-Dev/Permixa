# Current state

> Living operational status. **Update after every non-trivial implementation milestone.**
> Verified numbers only — re-run tests before changing baselines.

## Phase status

| Item | Value |
|------|--------|
| Last completed phase | **Phase I-3 — Optional Redis & Resend provider package split** |
| Current phase / work | None — await explicit instruction |
| Next approved implementation | **None** |
| Packaging | **Locally packable** (`0.1.0-preview.1`); **not publicly published** |

## Latest verified baseline

Date: **2026-09-14** (after Phase I-3 provider split)

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

## Product identity

| Item | Value |
|------|--------|
| Brand | Permixa |
| Product | Permixa IAM |
| Solution | `Permixa.slnx` |
| Primary NuGet package | `Permixa.AspNetCore` |
| Optional providers | `Permixa.Caching.Redis`, `Permixa.Email.Resend` |
| Preview version | `0.1.0-preview.1` |
| Local feed | `artifacts/local-feed/` (gitignored) |

## Packaging status

| Item | Status |
|------|--------|
| Local `dotnet pack` (6 packages) | Verified |
| Core install without Redis/Resend SDKs | Verified (nuspec + consumers A–D) |
| PackageReference consumers | Verified then deleted |
| Public nuget.org / GitHub Packages | **Not published** |
| License | **Not finalized** (omitted from local packages) |
| RepositoryUrl / SourceLink | Deferred |
| Package README / icon | Pre-public work |
| Classification | **Locally packable but not publication-ready** |

## Implemented modules (summary)

| Area | Status |
|------|--------|
| Domain / Application / Infrastructure / AspNetCore | Implemented |
| Optional Redis authorization-cache provider | Implemented (`Permixa.Caching.Redis`) |
| Optional Resend email transport provider | Implemented (`Permixa.Email.Resend`) |
| Provider-neutral `AddPermixaEmailDelivery` | Implemented |
| Local NuGet packages | Implemented (preview) |
| Production HTTP controllers in packages | Not implemented (by design) |

## Migrations

Under `Permixa.Infrastructure/Persistence/Migrations/` (unchanged through I-3 — **no schema change**):

1. `20260910144455_InitialCreate`
2. `20260910160808_AddRefreshTokenFamilyId`
3. `20260910170709_AddOpenVerificationChallengeUniqueIndex`
4. `20260913200000_AddUserIsDisabled`
5. `20260913210000_AddUserPendingEmail`
6. `20260913220000_AddMfaLoginChallenges`
7. `20260914120000_AddIamAuditLogs`

## Unresolved / deferred decisions

| Topic | Classification |
|-------|----------------|
| Permanent product license | Owner decision required before public publish |
| Canonical public repository URL | Deferred |
| SourceLink | Deferred |
| Package README / icon / public CHANGELOG | Pre-public |
| Move `IEmailSender` to Application | Deferred (acceptable in Infrastructure for now) |
| Unified `AddPermixa(...)` facade | Future DX decision (not approved) |
| NuGet prefix reservation / PackageId availability | Pre-public |

## Next documentation maintenance rule

When implementation resumes: update this file, [AGENT-HANDOFF.md](AGENT-HANDOFF.md), subsystem docs, and ADRs as required by `.cursor/rules/project-documentation.mdc`.
