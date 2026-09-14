# ADR-0010 — Dedicated MfaLoginChallenge

- **Status:** Accepted

## Context

Need a server-side pending MFA login proof after password success without issuing tokens.

## Decision

Dedicated `MfaLoginChallenge` table/entity: one row per user, replace in place, store proof hash only, lifetime + attempts + single-use consume.

## Verified rationale

Phase F implementation design (approved MFA v1 scope). Class documentation states raw proof never persisted.

## Consequences

Separate from `VerificationChallenge`. Concurrency handled via unique user row + hash concurrency + set-based consume.

## Alternatives considered

No historically verified alternatives recorded in durable form (e.g. storing challenges only in cache was not the adopted path).

## Source/evidence note

Phase F; `MfaLoginChallenge.cs`; migration `20260913220000_AddMfaLoginChallenges`.
