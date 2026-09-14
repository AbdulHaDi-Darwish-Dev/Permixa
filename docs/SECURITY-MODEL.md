# Security model

## Purpose

Summarize security invariants that future agents must not weaken without explicit approval.

## Authorization

- **Default deny** for permissions (`PermissionAuthorizationResolver`).
- Precedence: **User Deny > User Allow > Role grant > Default Deny**.
- **Permissions answer WHAT** the actor may do; **role hierarchy answers WHO** they may affect.
- User override **Allow** does not bypass hierarchy checks on administrative use cases that call `IAuthorizationHierarchyService`.
- Smaller `RoleLevel` = stronger authority; same tier cannot manage each other by default; Owner remains level **1** in bootstrap.
- Self-management of hierarchy-gated admin operations is denied (`AuthorizationHierarchyService`).

## Tokens and sessions

- Access tokens: short-lived JWT, **RS256**, external RSA key configuration.
- Claims philosophy (verified): identity/session claims as implemented — **no permission list in JWT**, **no RoleLevel in JWT**.
- Refresh tokens: hashed at rest, rotated, scoped to **FamilyId** (one logical login/session). Replay of a replaced token revokes the family.
- **No access-token blacklist** subsystem (historical direction: rely on short TTL + refresh/session revoke). Changing this requires approval.
- Account **disable** and **lockout** are distinct; disabled/locked users are rejected on auth flows as implemented.

## MFA

- Optional per-user MFA (Identity 2FA + Permixa login challenge).
- Successful password verification for MFA-enabled users returns **`MfaRequired`**, not tokens.
- Tokens issue only after successful MFA completion (TOTP or recovery code) via dedicated use cases.
- Raw MFA proof is not persisted; only proof hash. Secrets/codes never logged or audited.

## Verification

- Challenges use Identity token providers where designed; lifecycle includes expiry, attempts, cooldown/reissue as implemented.
- Anti-enumeration behavior exists where use cases intentionally return non-revealing outcomes — verify per use case before changing.

## Audit

- Security-relevant mutation audit via `IIamAuditSink`; default SQL sink.
- Append-only; no update/delete Application APIs.
- No destructive FKs from audit rows to IAM entities.
- Default SQL audit failure inside a mutation transaction → **fail closed** (rollback).
- Custom/external sinks: host owns delivery failure semantics; no Permixa outbox in Phase G.
- Audit read requires `Iam.Audit.Read`; paged; no hierarchy filtering for global audit readers (Phase G policy).

## Sensitive data / never log

Production logs and audit metadata must **never** contain:

```text
passwords / CurrentPassword / NewPassword / password hashes
raw AccessToken / raw RefreshToken / refresh-token hashes
full Authorization headers
MfaProof / TOTP codes / recovery codes / authenticator shared secret
verification tokens / OTP values
SecurityStamp / DataProtection tokens
```

Prefer identifiers: `UserId`, `RoleId`, `PermissionId`, `ChallengeId`, `FamilyId`.

Do not log request/command DTOs wholesale when they may contain secrets.

### Sensitive logging review status

| Status | Meaning |
|--------|---------|
| **Policy defined** | This document + development guidelines |
| **Focused review completed (Phase G)** | Known production `ILogger` sites reviewed; `SensitiveLoggingReviewTests` asserts message templates avoid forbidden tokens |
| **Not claimed** | Eternal solution-wide guarantee for every future log statement |

## Rate Limiting

- Opt-in via `AddPermixaRateLimiting` — does **not** globally throttle the host.
- Host-defined policy names and algorithms over ASP.NET Core native limiters.
- Partitioning: `RemoteIp` / `Global` / `AuthenticatedUserId` only; never secrets, raw tokens, or email/username as keys.
- Trust `Connection.RemoteIpAddress` (host configures forwarded headers); Permixa does not parse proxy headers.
- Process-local counters; no Redis/SQL rate-limit state; no IAM audit rows for every 429.
- HTTP 429 responses must not leak lockout / MFA / challenge / account-existence state.
- See [architecture/RATE-LIMITING.md](architecture/RATE-LIMITING.md) and [ADR-0013](decisions/ADR-0013-rate-limiting-configuration-layer.md).

## Owner and bootstrap

Bootstrap seeds Owner role (level 1), IAM permission catalog (including `Iam.Audit.Read`), and grants Owner the catalog. Do not weaken Owner protections or bootstrap idempotence without approval.

## Related

- [architecture/AUTHENTICATION.md](architecture/AUTHENTICATION.md)
- [architecture/AUTHORIZATION.md](architecture/AUTHORIZATION.md)
- [architecture/MFA.md](architecture/MFA.md)
- [architecture/AUDIT.md](architecture/AUDIT.md)
- [architecture/RATE-LIMITING.md](architecture/RATE-LIMITING.md)
