# Current state

> Living operational status. **Update after every non-trivial implementation milestone.**
> Verified numbers only — re-run tests before changing baselines.

## Phase status

| Item | Value |
|------|--------|
| Last completed phase | **Phase T3.1 — Templates-only publication workflow** |
| Current phase / work | Await owner Trusted Publishing policy + explicit publish approval |
| Next approved implementation | **None** |
| Framework packages | **Published** `0.1.0-preview.1` on nuget.org |
| Templates | **Ready for publication** via `publish-nuget-templates.yml`; **not published yet** |
| License | Apache-2.0 |
| Canonical repository | https://github.com/AbdulHaDi-Darwish-Dev/Permixa |

## Latest verified baseline

### Framework (historical I-5)

Date: **2026-09-14** (after Phase I-5) — re-run before claiming new totals:

```text
Domain                36
Application          224
Infrastructure       171
AspNetCore            52
Caching.Redis         17
Email.Resend           5
Integration           51
Total                556 / 556
```

### Templates (T3 — publication preparation)

Date: **2026-09-17** — metadata/README audited; local nupkg install + public nuget.org restore smoke; publish workflow recommendation prepared. **Not published.**

Prior T2.2 Docker baseline (still the verified test matrix):

| Variant | Domain | Application | Infrastructure | Integration | Total |
|---------|-------:|------------:|---------------:|------------:|------:|
| base | 2 | 1 | 1 | 4 | 8 |
| `--redis` | 2 | 1 | 1 | 5 | 9 |
| `--resend` | 2 | 1 | 1 | 4 | 8 |
| `--redis --resend` | 2 | 1 | 1 | 5 | 9 |

All **passed**, **0 skipped** (T2.2 / pre-T3 regression).

## Product identity

| Item | Value |
|------|--------|
| Brand | Permixa |
| Product | Permixa IAM |
| Solution | `Permixa.slnx` |
| Primary NuGet package | `Permixa.AspNetCore` |
| Optional providers | `Permixa.Caching.Redis`, `Permixa.Email.Resend` |
| Templates package | `Permixa.Templates` (`permixa-app`) |
| Preview version | `0.1.0-preview.1` |

## Migrations (framework)

Unchanged through T2:

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
| Publish `Permixa.Templates` to nuget.org | **Ready** — run `publish-nuget-templates.yml` after Trusted Publishing policy |
| Trusted Publishing for `publish-nuget-templates.yml` | **OWNER MUST VERIFY/ADD** on nuget.org (filename-bound; not proven by repo) |
| Package icon | Non-blocking |
| CLA / DCO | Before accepting substantial external PRs |
| Unified `AddPermixa(...)` facade | Deferred |
| Move `IEmailSender` to Application | Deferred |

## Next documentation maintenance rule

When implementation resumes: update this file, [AGENT-HANDOFF.md](AGENT-HANDOFF.md), subsystem docs, and ADRs as required by `.cursor/rules/project-documentation.mdc`.
