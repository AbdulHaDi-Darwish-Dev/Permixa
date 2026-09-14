# Phase 3 — Infrastructure + Identity + EF Core (+ 3.1 bootstrap)

## Goal

Implement Identity entities, EF Core SQL Server persistence, repositories, and secure IAM bootstrap.

## Important decisions (verified where noted)

- Runtime connection string required from host (Option A) — fail fast if missing.
- Design-time: `PERMIXA_CONNECTION_STRING` env var (Option A).
- Dev DB name `PermixaDb` approved for later local use.
- `ApplicationUser` / `ApplicationRole` with AuthorizationVersion / RoleLevel.
- Migration `InitialCreate`.

## Implemented

Infrastructure project, repositories, UnitOfWork, memory permission cache placeholder, AuthorizationState bootstrap, Phase 3.1 Owner + permission catalog seed.

## Excluded (at Phase 3 end)

JWT issuance, Redis, Resend, verification use-case completion, HTTP.

## Follow-up

Phase 4 Authentication.
