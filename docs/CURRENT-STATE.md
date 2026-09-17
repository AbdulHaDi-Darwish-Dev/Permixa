# Current state

> Living operational status. **Update after every non-trivial implementation milestone.**
> Verified numbers only — re-run tests before changing baselines.

## Phase status

| Item | Value |
|------|--------|
| Last completed phase | **Phase R2.1 — Preview.2 release lockdown** |
| Current phase / work | Await **owner publish approval** for `0.1.0-preview.2` |
| Next approved implementation | **None** |
| Framework packages on nuget.org | **Published** `0.1.0-preview.1` (until preview.2 ships) |
| Release candidate | **`0.1.0-preview.2` ready for publication** (seven packages + icon); **not published yet** |
| License | Apache-2.0 |
| Canonical repository | https://github.com/AbdulHaDi-Darwish-Dev/Permixa |

## Latest verified baseline

### Framework (R2 fresh local Release run — 2026-09-17)

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

### Templates (R2 local RC feed — `artifacts/r2-pack`, not nuget.org)

| Variant | Passed | Failed | Skipped |
|---------|--------|--------|---------|
| base (`ClinicSystem`) | 8 | 0 | 0 |
| `--redis` | 9 | 0 | 0 |
| `--resend` | 8 | 0 | 0 |
| `--redis --resend` | 9 | 0 | 0 |
| Rename (`HealthPortal`, `Acme.Identity.Api`) | build OK | — | — |

Generated apps pin Permixa packages to `0.1.0-preview.2`. Public nuget.org restore for preview.2 is **not** claimed until publish succeeds.

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
| Preview version | `0.1.0-preview.2` (RC) |

## Migrations (framework)

Unchanged through R2:

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
| Publish `0.1.0-preview.2` (seven packages) | Owner approval required |
| NuGet Trusted Publishing scope on `publish-nuget.yml` | **OWNER MUST VERIFY** covers all seven packages including `Permixa.Templates` (repo: `AbdulHaDi-Darwish-Dev/Permixa`) |
| Preview publish train | **Lockstep only** — sole workflow `publish-nuget.yml` (Templates-only workflow removed) |
| Package icon | **Resolved** (`assets/permixa-icon.png`) |
| CLA / DCO | Before accepting substantial external PRs |
| Unified `AddPermixa(...)` facade | Deferred |
| Move `IEmailSender` to Application | Deferred |

## Next documentation maintenance rule

When implementation resumes: update this file, [AGENT-HANDOFF.md](AGENT-HANDOFF.md), subsystem docs, and ADRs as required by `.cursor/rules/project-documentation.mdc`.
