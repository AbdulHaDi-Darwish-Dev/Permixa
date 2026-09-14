# Phase I-2 — NuGet Packaging & Local Package Consumer Validation

## Goal

Prove Permixa can be consumed as real NuGet packages (`PackageReference` only) from a local feed. **Local only — not publicly published.**

## Outcome

| Item | Result |
|------|--------|
| Packages | `Permixa.Domain` / `Application` / `Infrastructure` / `AspNetCore` @ `0.1.0-preview.1` |
| Local feed | `artifacts/local-feed/` (gitignored) |
| Primary install | `Permixa.AspNetCore` pulls Application + Infrastructure (+ Domain transitively) |
| Consumer | Disposable PackageReference host under `artifacts/package-consumer/` — **deleted** after validation |
| License | Omitted (not finalized) |
| RepositoryUrl / SourceLink | Deferred |
| Classification | **Locally packable but not publication-ready** |

## Consumer validation covered

Migrate, bootstrap×2, Owner `EmailConfirmed` + login with `RequireConfirmedEmail`, register + OTP confirmation (embedded templates), refresh/logout, roles/permissions, audit read, MFA enable + recovery completion, Rate Limiting 429, JWT + `RequirePermission`, unauthorized ProblemDetails path.

## Verified after phase

```text
Domain           36
Application     224
Infrastructure  187
AspNetCore       52
Integration      51
Total           550 / 550

Build: 0 errors, 0 warnings
Skipped: 0
```

Package consumer: `PACKAGE_CONSUMER_VALIDATION_PASSED` then deleted.

Recorded in `CURRENT-STATE.md` / `AGENT-HANDOFF.md`.

