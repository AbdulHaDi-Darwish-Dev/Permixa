# ADR-0005 — AuthorizationVersion + RbacVersion; Redis disposable cache

- **Status:** Accepted

## Context

Need cache invalidation when user-specific or global RBAC data changes without making Redis authoritative.

## Decision

- Per-user `AuthorizationVersion` on `ApplicationUser`
- Global `RbacVersion` on singleton `AuthorizationState`
- Redis (or memory) holds disposable `AuthorizationSnapshot`; SQL is source of truth; cache failures are misses/no-ops

## Verified rationale

Phase 5 Redis cache phase and earlier Application core snapshot design. Explicit: Redis is not source of truth.

## Consequences

Different mutations bump different versions. Agents must not unify them casually. Audit must not bump versions by itself.

## Alternatives considered

No historically verified alternatives recorded.

## Source/evidence note

Phase 5 prompts/reports; `EffectivePermissionService`; `RedisPermissionCache`.
