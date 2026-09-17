# AGENTS.md

Portable engineering guidance for humans and coding agents working in **this application**.

## Non-negotiables

1. Respect Clean Architecture dependency direction:
   - Domain ← Application ← Infrastructure
   - Api → Application + Infrastructure
2. Domain must not reference EF Core, ASP.NET Core, Permixa, or HTTP types.
3. Application must not reference EF Core or ASP.NET Core.
4. Consume Permixa only via NuGet (`Permixa.AspNetCore` and optional providers). Do not copy Permixa source into this repo.
5. Prefer set-based database access. Avoid N+1 queries.
6. Do not call `SaveChanges` / `SaveChangesAsync` repeatedly inside loops when one unit of work can commit all changes.
7. `Task.WhenAll` over N per-item database queries is still N queries — not an N+1 fix.
8. Do not silently make meaningful architecture, security, schema, or public-API decisions. Stop and ask.
9. Keep tests aligned with behavior changes.
10. Update authoritative docs (`docs/CURRENT-STATE.md`, `docs/AGENT-HANDOFF.md`, ADRs when approved) after non-trivial changes.
11. Do not over-engineer. Prefer the smallest clear design that fits this solution.
12. `Reference/SampleNotes` is disposable teaching code — do not build the product around it.

## Persistence ownership

- Permixa `ApplicationDbContext` owns IAM schema (package migrations).
- This app's `AppDbContext` owns business schema (migrations under Infrastructure, history table `__AppMigrationsHistory`).
- Do not put business entities on Permixa's DbContext.

## Secrets

Never commit RSA PEMs, passwords, Resend API keys, or production connection strings.
