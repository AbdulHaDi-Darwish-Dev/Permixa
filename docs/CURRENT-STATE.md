# Current state

> Living operational status. **Update after every non-trivial implementation milestone.**
> Verified numbers only — re-run tests before changing baselines.

## Phase status

| Item | Value |
|------|--------|
| Last completed phase | **Phase I-5 — Public Preview Preparation & Final Dry Run** |
| Current phase / work | None — await **explicit NuGet publish approval** |
| Next approved implementation | **None** |
| Packaging | **Public-preview ready pending explicit publish approval** |
| License | Apache-2.0 |
| Canonical repository | https://github.com/AbdulHaDi-Darwish-Dev/Permixa |

## Latest verified baseline

Date: **2026-09-14** (after Phase I-5)

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
| Local `dotnet pack` (6 packages) | Verified (I-5 dry-run) |
| License / RepositoryUrl / SourceLink / READMEs | Configured (ADR-0017) |
| Validation CI workflow | Present (no publish) |
| Package icon | Deferred |
| Public nuget.org | **Not published** |
| Classification | **Public-preview ready pending explicit publish approval** |

## Migrations

Unchanged through I-5 (no schema change in this phase):

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
| NuGet.org publish of `0.1.0-preview.1` | Owner approval required |
| `Permixa.*` prefix reservation | After first publish |
| Package icon | Non-blocking |
| CLA / DCO | Before accepting substantial external PRs |
| Unified `AddPermixa(...)` facade | Deferred |
| Move `IEmailSender` to Application | Deferred |

## Next documentation maintenance rule

When implementation resumes: update this file, [AGENT-HANDOFF.md](AGENT-HANDOFF.md), subsystem docs, and ADRs as required by `.cursor/rules/project-documentation.mdc`.
