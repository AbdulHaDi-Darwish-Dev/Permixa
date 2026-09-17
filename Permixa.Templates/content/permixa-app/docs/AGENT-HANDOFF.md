# Agent handoff

> **Purpose:** Concise entry point for AI agents and new developers.  
> **Authority:** Operational orientation — link out for depth.  
> **Update when:** Status, next work, or critical invariants change.

## Read first

1. This file  
2. [ARCHITECTURE.md](ARCHITECTURE.md)  
3. [SECURITY.md](SECURITY.md)  
4. Root [AGENTS.md](../AGENTS.md)  
5. [DEVELOPMENT-GUIDE.md](DEVELOPMENT-GUIDE.md)

## Identity

Consumer ASP.NET Core app using Permixa NuGet IAM. Clean Architecture host.

## Critical invariants

- Two DbContexts; do not merge business entities into Permixa context
- Development-only auto-migrate
- No committed secrets
- SampleNotes is reference-only

## Current / next

See [CURRENT-STATE.md](CURRENT-STATE.md).
