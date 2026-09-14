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

## Publication

**Not published.** Classification after dry-run: see CURRENT-STATE / final report.
