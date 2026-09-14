# ADR-0013 — Rate Limiting as opt-in AspNetCore configuration layer

- **Status:** Accepted
- **Date context:** Phase I-0 (2026-09-14)

## Context

Hosts need reusable rate limiting for IAM and non-IAM endpoints without Permixa silently throttling the entire application or forcing a fixed IAM policy/algorithm catalog.

## Decision

1. Rate Limiting belongs in **`Permixa.AspNetCore`** (future package brand Permixa.AspNetCore) — not Domain/Application/Infrastructure, and not a separate RateLimiting package.
2. Registration is **opt-in** via `AddPermixaRateLimiting` — not auto-wired from other `AddPermixa*` methods.
3. **No global limiter** — only developer-applied named policies affect endpoints.
4. Permixa is a **type-safe configuration layer** over ASP.NET Core native Rate Limiting (`FixedWindow`, `SlidingWindow`, `TokenBucket`, `Concurrency`).
5. **Policy names are host-defined** and used unchanged (no automatic `Permixa.` / `Permixa.` prefix).
6. Algorithms are **chosen by the host**; documentation may guide but must not hard-code Login→Sliding, OTP→TokenBucket, etc.
7. **QueueLimit is configurable** (default 0); recommend 0 for security-sensitive ops.
8. Partitioning uses built-in kinds: `RemoteIp`, `Global`, `AuthenticatedUserId` — no custom key delegates in v1; no secrets/email as keys; no DB lookups; trust resolved connection IP (host configures forwarded headers).
9. **Process-local** counters only — no Redis/SQL/distributed rate-limit state; do not reuse authorization Redis cache.
10. **No IAM audit amplification** for every 429; no authorization version bumps; no secret logging.
11. Public C# API names remain **`Permixa*`** for this phase (no Permixa* aliases).

## Consequences

Hosts must call `AddPermixaRateLimiting`, `UseRateLimiter`, and apply policies explicitly. Multi-instance global quotas require external edge/gateway controls.

## Alternatives considered

- Fixed IAM policy catalog with mandated algorithms — rejected in revised Phase I-0 product requirement
- Dual Permixa/Permixa public APIs — rejected (no aliases)
- Redis/SQL distributed counters — out of scope
- Custom partition-key delegates — deferred; hosts use native ASP.NET Core APIs

## Source/evidence note

Phase I-0 revised product requirement (owner-approved Option A naming + generic configuration API).
