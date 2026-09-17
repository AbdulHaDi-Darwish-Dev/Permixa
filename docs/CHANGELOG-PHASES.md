# Phase changelog

Index of major Permixa phases. Detailed notes live under [phases/](phases/).

**Evidence note:** Git history was unavailable in the workspace when this was authored. Sequence and decisions come from implementation prompts/reports cross-checked against the current repository. Where rationale is missing: marked as not verified.

## Numbered early phases (historically labeled “foundation phases”)

> Early numbered phases ran under the internal name Foundation; product identity is now Permixa (Phase I-1 / ADR-0014).

| Phase | Title | Outcome |
|-------|--------|---------|
| 1 | Domain layer | Domain entities for permissions, refresh, verification |
| 1.1 | Identity alignment | Removed Domain User/Role/UserRole; Identity owns those |
| 2.1 | Application core | Abstractions, Result, hierarchy primitives |
| 2.2 | Authorization use cases | Permission/role-permission/override use cases |
| 3 | Infrastructure + Identity + EF | `ApplicationUser`/`Role`, DbContext, InitialCreate |
| 3.1 | Secure IAM bootstrap | Owner, catalog seed, grants |
| 4 | Authentication core | JWT RS256, refresh tokens |
| 4.1 | Auth hardening | Rotation/family hardening as specified then |
| 5 | Redis versioned authorization cache | Disposable snapshot cache |
| 6 | Verification use cases | Challenge orchestration + Identity tokens |
| 7 | Resend + email templates | Delivery + HTML/TXT |
| 8 | ASP.NET Core integration | Bearer JWT, permission handler, problem details |
| 9 | Integration scenarios | Composed host tests |
| 9.5 | IAM management completeness inspection | Planning only; proposed letter-phase roadmap |
| 10 | Packaging inspection | **Paused** — inspection only, no packaging |

## Letter phases (post–9.5)

Early 9.5 draft labeled MFA as “E” and Phone/SMS as “F”. **Actual executed order:**

| Phase | Title | Outcome |
|-------|--------|---------|
| A | Authorization administration completion | Catalog/admin completion work as scoped then |
| B | Role + UserRole administration | Hierarchy create/rename/reposition/delete; assign/remove |
| C | User administration + account state | Create/lock/disable/enable |
| D | Session administration | List/revoke self and admin |
| E | Credentials & email change security | Password change, pending email, force reset |
| F | MFA v1 | TOTP/recovery + LoginResult Option A |
| G | IAM audit | `IIamAuditSink` + SQL + read API |
| H | Disposable consumer DX validation | Temporary host validated + **deleted**; Owner/`RequireConfirmedEmail` blocker later fixed (ADR-0012) |
| H+ | Bootstrap Owner EmailConfirmed | Option A: new Owner `EmailConfirmed=true` |
| I-0 | Reusable Rate Limiting (AspNetCore) | Opt-in type-safe config over native ASP.NET Core Rate Limiting (ADR-0013); packaging still paused |
| I-1 | Foundation → Permixa branding migration | Projects/namespaces/public APIs renamed before packaging (ADR-0014) |
| I-2 | Local NuGet packaging + package consumer | `0.1.0-preview.1` local feed; PackageReference consumer validated + deleted (ADR-0015); **not publicly published** |
| I-3 | Optional Redis / Resend provider packages | `Permixa.Caching.Redis` + `Permixa.Email.Resend`; core no longer pulls those SDKs (ADR-0016) |
| I-4 | Public preview readiness assessment | Assessment only — blockers: license, repo, README, SourceLink, CI |
| I-5 | Public preview preparation + dry-run | Apache-2.0, canonical GitHub repo metadata, SourceLink, public docs, validation CI; later published as `0.1.0-preview.1` |
| T1 | Permixa .NET template design | Full Clean Architecture consumer template design approved |
| T2 | Permixa.Templates local implementation | `permixa-app` template packable locally; four variants validated for generate/build; **not published** |
| T2.1 | Template verification & security audit | Docker/Testcontainers: all four variants green; AppPermissionSeeder audit **B**; JSON/`AddPermixaVerification` host fixes; **not published** |
| T2.2 | Template final hardening | Seeder fail-fast AuthorizationState; AppSeed default false; `safe_namespace` for hyphenated `-n`; regression green; **not published** |
| T3 | Templates publication preparation | Metadata/README/nupkg consumer smoke; **not published** |
| T3.1 | Templates-only publication workflow | Added then **removed in R2.1** — preview uses lockstep `publish-nuget.yml` only |
| R2 | Unified preview.2 branding + release prep | Version `0.1.0-preview.2`, shared icon, Templates in main publish workflow; **RC not published** |
| R2.1 | Preview.2 release lockdown | Removed `publish-nuget-templates.yml`; lockstep-only publish train; **not published** |

Phone/SMS (once floated as “Phase F” in inspection notes) remains **deferred**.

## Latest verified baseline

See [CURRENT-STATE.md](CURRENT-STATE.md). R2 re-verified framework **556/556** and template Docker/Testcontainers matrix against local `0.1.0-preview.2` packs.
