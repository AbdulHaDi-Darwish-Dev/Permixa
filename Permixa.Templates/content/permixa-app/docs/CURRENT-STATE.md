# Current state

> **Purpose:** Living operational status.  
> **Authority:** Verified status only — re-run tests before changing baselines.  
> **Update when:** After every non-trivial milestone.

## Status

| Item | Value |
|------|--------|
| Template origin | `dotnet new permixa-app` |
| Framework | .NET 8 + Permixa `0.1.0-preview.2` |
| Reference feature | `Reference/SampleNotes` Create/Get/List only (safe to delete) |
| Authz cache | In-memory by default; Redis when generated with `--redis` |
| Email confirmation | Off by default; `RequireConfirmedEmail=true` with `--resend` |

## Next work

- Replace SampleNotes with real domain modules
- Disable bootstrap after first Owner creation
- Choose application license
