# Phase I-5 — Public Preview Preparation & Final Dry Run

## Goal

Make Permixa technically and publicly ready for `0.1.0-preview.1` without publishing.

## Approved inputs

- License: **Apache-2.0**
- Repository: `https://github.com/AbdulHaDi-Darwish-Dev/Permixa`
- Version: `0.1.0-preview.1`
- No `nuget push`

## Delivered

| Item | Result |
|------|--------|
| `LICENSE` / `NOTICE` | Apache-2.0 |
| Root `.gitignore` | Added |
| Root README | Consumer-facing product README |
| `SECURITY.md` / `CHANGELOG.md` / `CONTRIBUTING.md` | Added |
| Package READMEs + `PackageReadmeFile` | All six packages |
| SourceLink + repository metadata | Directory.Build.props |
| Validation CI | `.github/workflows/ci.yml` (no publish) |
| ADR | [ADR-0017](../decisions/ADR-0017-public-preview-packaging.md) |
| Dry-run pack | Six `.nupkg` + six `.snupkg` in `artifacts/local-feed/` |
| PackageReference consumers | Core / Redis / Resend validated then **deleted** |
| Vulnerability scan | No runtime vulnerable packages; SourceLink build-time `Microsoft.Build.Tasks.Git` 8.0.0 Moderate (PrivateAssets) |
| PackageId availability | All six **AVAILABLE** on nuget.org (404) |

## Dry-run regression

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

## Publication

**Not published.**

**Classification:** Public-preview ready pending explicit publish approval.
