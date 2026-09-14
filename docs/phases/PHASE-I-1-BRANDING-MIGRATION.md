# Phase I-1 — Foundation → Permixa Branding & Public API Migration

## Goal

Align source projects, namespaces, and public CLR APIs with the permanent product brand **Permixa** before first NuGet packaging.

## Outcome

- Projects/folders/solution renamed to `Permixa.*` / `Permixa.slnx`
- Namespaces and product-branded types/extensions renamed
- Approved technical string contracts updated (Redis, Resend idempotency, policy prefix, design-time env)
- ProblemDetails codes made brand-neutral (`InternalError`, `Concurrency.Conflict`, `Email.DeliveryFailed`)
- EF snapshot FQNs updated; migration IDs/history preserved; no branding SQL migration
- Docs/ADR-0014 updated; packaging still paused

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

EF: `dotnet ef migrations list` discovers all seven migrations; `has-pending-model-changes` reports no model changes from the rename.

Recorded in `CURRENT-STATE.md` / `AGENT-HANDOFF.md`.
