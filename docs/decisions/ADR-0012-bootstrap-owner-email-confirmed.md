# ADR-0012 — Bootstrap Owner EmailConfirmed

- **Status:** Accepted
- **Date context:** Phase H follow-up (2026-09-14)

## Context

Phase H consumer validation found that Bootstrap creates Owner with `EmailConfirmed = false`. With host `RequireConfirmedEmail = true`, Owner cannot log in, and Bootstrap does not expose a public Owner confirmation path without Identity/DbContext internals.

## Decision

A **newly created** Bootstrap Owner is provisioned with `EmailConfirmed = true` (administratively trusted).

This does **not**:

- auto-confirm ordinary registered users
- change `RequireConfirmedEmail` login semantics
- add Owner-login exceptions
- silently repair an existing Owner’s `EmailConfirmed` on idempotent Bootstrap re-runs

## Verified rationale

Approved Option A after Phase H: Bootstrap Owner is not a self-registered user; it is the initial trusted administrative identity.

## Consequences

Fresh Bootstrap + `RequireConfirmedEmail = true` allows Owner login. Hosts that previously created unconfirmed Owners and flipped the flag manually are unchanged by re-bootstrap.

## Alternatives considered

- Return OwnerUserId for host confirmation flow (Option B) — not chosen
- Document RequireConfirmedEmail=false until manual confirm (Option C) — weak DX
- Public confirm-by-email (Option D) — not chosen

## Source/evidence note

Phase H report; user approval of Option A; `PermixaBootstrapper.EnsureOwnerUserAsync`.
