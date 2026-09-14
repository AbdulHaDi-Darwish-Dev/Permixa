# ADR-0003 — Permissions and RoleLevel not in JWT

- **Status:** Accepted

## Context

JWT access tokens must not become stale permission documents.

## Decision

Do not embed permission lists or RoleLevel in JWT claims. Resolve authorization server-side using SQL (+ disposable Redis snapshot).

## Verified rationale

Repeated explicit project constraints across authentication and authorization phases; tests assert versions/permissions are not stuffed into tokens where covered.

## Consequences

Access tokens stay small; permission changes take effect without waiting for JWT claim refresh (subject to access-token TTL). Requires server-side effective permission checks.

## Alternatives considered

No historically verified alternatives recorded.

## Source/evidence note

Phase prompts for authentication/authorization; JWT generator claim set; MFA/auth tests asserting absence of version claims where applicable.
