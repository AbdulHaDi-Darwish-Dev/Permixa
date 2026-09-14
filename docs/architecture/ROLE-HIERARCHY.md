# Role hierarchy

## Status

**Implemented.** This document describes the **current** algorithm.

## Rules

From `RoleHierarchyRules` and related services:

- Smaller `RoleLevel` number = **higher** authority.
- `RoleLevel` ≥ 1.
- Bootstrap **Owner** = level **1**.
- Same `RoleLevel` = same hierarchy tier; same-tier actors cannot manage each other by default (`actorLevel < targetLevel` required).
- Effective user level = **MIN** of assigned role levels.
- No roles ⇒ no hierarchical authority (`null` effective level).
- Self-management of hierarchy-gated admin ops is denied.
- Roleless targets are manageable by any actor who has an effective level (see `AuthorizationHierarchyService` comments).

## Placement

`RolePlacement`: `Above` | `Below` | `SameLevel` (relative to a reference role).

## Collision-only shifting (current)

`RolePlacementCalculator` documents:

> Pure collision-only RoleLevel placement. **No midpoint and no spacing constant.**  
> Shifts move only toward weaker authority (+1) and stop at the first free integer.

- `SameLevel`: assign reference level; no shift chain.
- Occupied target tiers shift **+1 toward weaker** authority.
- Infrastructure `IdentityRoleWriter.ShiftTiersAsync` applies shifts from the weaker end first.

One logical create/reposition emits **one** audit event even if multiple tiers shift (Phase G).

## Rejected / not current approaches

Code explicitly rejects midpoint/spacing design in comments. Early inspection transcripts mentioned denser rebalance ideas in planning discussions; **current architecture is collision-only**.

If implementing a different scheme: **STOP** for approval. See [ADR-0004-collision-only-role-levels.md](../decisions/ADR-0004-collision-only-role-levels.md).

## Concurrency

Hierarchy writes use an exclusive lock over the singleton `AuthorizationState` row (`IAuthorizationHierarchyWriteLock`) for create/reposition paths that may shift tiers. Rationale for lock details: see implementation and tests; do not change without approval.

## Related

- [AUTHORIZATION.md](AUTHORIZATION.md)
- Implemented in: `RoleHierarchyRules`, `RolePlacementCalculator`, `CreateRoleUseCase`, `ChangeRolePositionUseCase`, `IdentityRoleWriter`
