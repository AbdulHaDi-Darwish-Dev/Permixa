# ADR-0004 — Collision-only RoleLevel placement

- **Status:** Accepted

## Context

Roles need ordered hierarchy authority with placement Above/Below/SameLevel relative to a reference role.

## Decision

Use integer `RoleLevel` with collision-only shifting: move occupied tiers +1 toward weaker authority until a free integer exists. No midpoint algorithm; no spacing constant (e.g. S=1024) in current design.

## Verified rationale

`RolePlacementCalculator` documents “No midpoint and no spacing constant.” Smaller number = higher authority is explicit in `RoleHierarchyRules`.

## Consequences

Dense integer levels; shift chains possible on create/reposition; exclusive hierarchy write lock used for shift paths.

## Alternatives considered

Midpoint / spacing schemes are explicitly excluded by current code comments. Detailed historical debate text for S=1024-style approaches was not fully recovered as a formal ADR discussion; treat midpoint/spacing as **not current**, not as a fully reconstructed rejected design history.

## Source/evidence note

`RolePlacementCalculator.cs`, `RoleHierarchyRules.cs`, role administration use cases/tests.
