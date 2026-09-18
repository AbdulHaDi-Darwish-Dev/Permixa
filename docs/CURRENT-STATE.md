# Current state

> Living operational status. **Update after every non-trivial implementation milestone.**
> Verified numbers only — re-run tests before changing baselines.

## Phase status

| Item | Value |
|------|--------|
| Last completed phase | **Phase R3 — Preview.2 post-release finalization** |
| Current phase / work | **None** — await next owner-approved work |
| Next approved implementation | **None** |
| nuget.org | **Published** `0.1.0-preview.2` (all seven packages) |
| GitHub Release | `v0.1.0-preview.2` (pre-release) |
| License | Apache-2.0 |
| Canonical repository | https://github.com/AbdulHaDi-Darwish-Dev/Permixa |
| Published source commit | `af9de3cf77dcc83333bb4252b0295a7da774809e` |

## Latest verified baseline

### Framework (R2 fresh local Release run — 2026-09-17; re-confirmed green on publish CI)

| Suite | Passed | Failed | Skipped |
|-------|--------|--------|---------|
| Domain | 36 | 0 | 0 |
| Application | 224 | 0 | 0 |
| Infrastructure | 171 | 0 | 0 |
| AspNetCore | 52 | 0 | 0 |
| Caching.Redis | 17 | 0 | 0 |
| Email.Resend | 5 | 0 | 0 |
| Integration | 51 | 0 | 0 |
| **Total** | **556** | **0** | **0** |

### Templates

| Check | Result |
|-------|--------|
| Local RC matrix (R2) | base 8 / `--redis` 9 / `--resend` 8 / full 9 |
| Public consumer smoke | `dotnet new install Permixa.Templates@0.1.0-preview.2` → generate → **build OK** |

Generated apps pin Permixa packages to `0.1.0-preview.2` from nuget.org.

## Product identity

| Item | Value |
|------|--------|
| Brand | Permixa |
| Product | Permixa IAM |
| Package icon | `assets/permixa-icon.png` (all seven packages) |
| Solution | `Permixa.slnx` |
| Primary NuGet package | `Permixa.AspNetCore` |
| Optional providers | `Permixa.Caching.Redis`, `Permixa.Email.Resend` |
| Templates package | `Permixa.Templates` (`permixa-app`) |
| Preview version | `0.1.0-preview.2` (**published**) |

## Migrations (framework)

Unchanged through preview.2:

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
| Preview publish train | **Lockstep only** — sole workflow `publish-nuget.yml` |
| Package icon | **Resolved** (`assets/permixa-icon.png`) |
| CLA / DCO | Before accepting substantial external PRs |
| Unified `AddPermixa(...)` facade | Deferred |
| Move `IEmailSender` to Application | Deferred |

## Next documentation maintenance rule

When implementation resumes: update this file, [AGENT-HANDOFF.md](AGENT-HANDOFF.md), subsystem docs, and ADRs as required by `.cursor/rules/project-documentation.mdc`.
