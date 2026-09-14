# Phase 2 — Application (2.1 core + 2.2 authorization use cases)

## Goal

Application abstractions and authorization use cases without Infrastructure.

## Approved scope

- 2.1: Result/Error, clock/UoW abstractions, Identity readers (interfaces), permission repositories, effective permission + hierarchy primitives, refresh/verification abstractions.
- 2.2: Create/get permissions, assign/remove role permissions, set/remove overrides, IAM permission constants, hierarchy service wiring.

## Important decisions

Hierarchy rules: smaller RoleLevel = stronger; permissions vs hierarchy separation.

## Excluded

EF, Identity packages, Redis, JWT implementations.

## Follow-up

Phase 3 Infrastructure.
