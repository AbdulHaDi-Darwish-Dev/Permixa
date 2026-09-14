# ADR-0015 — Local NuGet packaging for Permixa preview

- **Status:** Accepted
- **Date context:** Phase I-2 (2026-09-14)

## Context

After the Foundation → Permixa branding migration (ADR-0014), Permixa needed proof that it can be consumed as real NuGet packages via `PackageReference` without source `ProjectReference`.

Public publication was not approved. License / repository URL / SourceLink remain unresolved for public release.

## Decision

1. **PackageIds** (permanent intent):
   - `Permixa.Domain`
   - `Permixa.Application`
   - `Permixa.Infrastructure`
   - `Permixa.AspNetCore` (primary consumer-facing package)
2. **Authors:** `Abdulhadi Darwish`
3. **Copyright:** `Copyright (c) 2026 Abdulhadi Darwish`
4. **Local preview version:** `0.1.0-preview.1`
5. **License:** not finalized — omit license metadata for local-only packs; do not invent MIT/open-core/commercial choice
6. **RepositoryUrl / PackageProjectUrl / SourceLink:** deferred (do not fabricate)
7. **Public publication:** not approved (no nuget.org / GitHub Packages push)
8. Shared metadata lives in `Directory.Build.props`; per-package Title/Description/Tags in each library `.csproj`
9. XML documentation generation deferred (would introduce many CS1591 warnings without global suppression)
10. Symbol packages (`.snupkg`) generated without SourceLink

## Consequences

Permixa is **locally packable** and validated via a disposable PackageReference consumer. It is **not** publication-ready until license, canonical repository, SourceLink, package README, and related release items are approved.

## Source/evidence note

Phase I-2 owner prompt; local feed under `artifacts/local-feed/` (gitignored); disposable consumer deleted after validation.
