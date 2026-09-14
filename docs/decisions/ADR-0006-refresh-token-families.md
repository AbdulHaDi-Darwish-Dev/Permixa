# ADR-0006 — Refresh-token FamilyId session model

- **Status:** Accepted

## Context

Need rotation-safe refresh tokens and multi-session revoke without ASP.NET Session.

## Decision

Each login issues a refresh token with a **FamilyId**. Rotation keeps FamilyId. Replay of a replaced token revokes the family. Sessions are families.

## Verified rationale

Authenticated design in authentication phases; migration `AddRefreshTokenFamilyId`; `AuthenticationTokenService` replay handling.

## Consequences

Logout/revoke operate on families. No device metadata required for v1.

## Alternatives considered

No historically verified alternatives recorded.

## Source/evidence note

Migration `20260910160808_AddRefreshTokenFamilyId`; `RefreshToken` entity; token service.
