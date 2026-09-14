# ADR-0017 — Apache-2.0 core license, canonical GitHub repo, and public preview packaging

- **Status:** Accepted
- **Date context:** Phase I-5 (2026-09-14)

## Context

Phase I-4 established that Permixa was locally packable but not publication-ready without license, canonical repository metadata, SourceLink, public README, and validation CI.

## Decision

1. **License:** Apache License 2.0 for Permixa Core (all six current packages). Root `LICENSE` + `PackageLicenseExpression=Apache-2.0`.
2. **Business model note (non-code):** Core is open source; future Enterprise/Cloud products (if any) may be proprietary and are **not** covered by current package licenses. No dual-licensing implemented.
3. **Canonical repository:** `https://github.com/AbdulHaDi-Darwish-Dev/Permixa` for `RepositoryUrl` and `PackageProjectUrl`.
4. **SourceLink:** `Microsoft.SourceLink.GitHub` with `PublishRepositoryUrl` / `EmbedUntrackedSources`; CI sets `ContinuousIntegrationBuild`.
5. **CI:** Validation-only GitHub Actions (`build` / `test` / `pack` / graph inspect). **No** `nuget push`.
6. **Package READMEs:** AspNetCore uses root README; layer packages share a short README; providers have focused READMEs.
7. **Icon:** omitted for this preview (non-blocking).
8. **Prefix reservation:** deferred until after first publication.

## Consequences

Packages can be prepared for an explicit owner-approved NuGet publish of `0.1.0-preview.1`. Publication itself remains a separate approval.

## Source/evidence note

Phase I-5 owner prompt; Apache-2.0 and repository URL explicitly approved.
