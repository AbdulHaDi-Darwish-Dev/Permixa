# Phase 4 — Authentication core (+ 4.1 hardening)

## Goal

JWT access tokens + stateful refresh tokens with rotation/family model.

## Important decisions

- RS256 with external keys
- Short-lived access tokens
- Refresh hash storage, rotation, FamilyId
- No permissions/RoleLevel in JWT
- No access-token blacklist direction

## Implemented

Token generator, refresh repository usage, login/refresh/logout orchestration as scoped then; FamilyId migration.

## Follow-up

Phase 5 Redis cache.
