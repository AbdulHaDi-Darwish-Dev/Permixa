# Sessions

## Status

**Implemented.** Permixa “sessions” are **refresh-token families**, not ASP.NET Session state.

## Model

- One successful login (or MFA completion that issues tokens) → one **FamilyId**.
- Refresh rotation stays in the same family.
- New login → new family.
- Logout / revoke session → revoke that family (and related refresh rows as implemented).
- Multiple concurrent sessions (families) per user are supported.
- Session listing and admin/self revoke use cases exist (Phase D).

## Device metadata

**Current implementation fact:** no rich device/browser metadata model is part of Permixa session entities. Do not invent device tracking without approval.

## Access-token behavior

Revoking a family does **not** blacklist already-issued access JWTs. Access tokens remain valid until expiry (no blacklist — see Authentication ADR). Hosts needing immediate access kill must use very short TTL or propose a new approved design.

## Audit

Logical revoke commands emit one audit event each (not one row per refresh token). Logout audited via `AuthenticationTokenService` as `Session.Logout`.

## Related

- [AUTHENTICATION.md](AUTHENTICATION.md)
- Tests: session administration Infrastructure/Application tests; integration flows
