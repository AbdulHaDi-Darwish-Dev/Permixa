# Phase 1 — Domain layer

## Goal

Create a framework-independent Domain model for IAM concepts Identity does not provide.

## Approved scope

Domain only: permissions, overrides, authorization state, refresh token entity, verification challenge/enums, domain exception. No Application/Infrastructure.

## Important decisions

- Initially included User/Role/UserRole (**later reversed** in Phase 1.1).
- PermissionEffect Allow/Deny; resolver default deny.

## Implemented

Domain entities/tests as of Phase 1 completion report.

## Excluded

Application, Infrastructure, Redis, JWT, HTTP.

## Final baseline (historical report)

As reported at Phase 1 completion: Domain tests green (exact count evolved; do not treat early counts as current). See [CURRENT-STATE.md](../CURRENT-STATE.md) for live totals.

## Follow-up

Phase 1.1 Identity alignment.
