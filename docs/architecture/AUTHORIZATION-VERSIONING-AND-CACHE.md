# Authorization versioning and cache

## Status

**Implemented.**

## Versions

| Version | Storage | Purpose |
|---------|---------|---------|
| `ApplicationUser.AuthorizationVersion` | Identity user column | Invalidate per-user authorization snapshots when **that user’s** grants/overrides/roles change |
| `AuthorizationState.RbacVersion` | Singleton domain row | Invalidate when **global RBAC** structure changes (role-permission grants; hierarchy shifts that affect RBAC) |

Both start at **1**. User version is an EF concurrency token.

## What bumps what (current implementation fact)

**Typically bumps `AuthorizationVersion` (via `IUserAuthorizationVersionStore.IncrementAsync`):**

- Assign/remove role to/from user (when mutation actually changes membership)
- Set/remove user permission override (when create/effect change/remove occurs)

**Typically bumps `RbacVersion`:**

- Assign/remove permission to/from role
- Create role when placement requires a shift; change role position when not a no-op

**Does not bump versions merely because audit wrote a row** (Phase G invariant).

Permission catalog create/update description, role rename/delete, MFA/password/email flows: do **not** assume version bumps — verify the specific use case before documenting otherwise.

## AuthorizationSnapshot + Redis

- Snapshot carries user id, permission set, and version pair used for freshness checks.
- **Redis** (`RedisPermissionCache`): disposable cache. On corrupt/timeout/failure → treat as miss / best-effort no-op write.
- **SQL remains source of truth.**
- Redis is **not** used for refresh tokens, sessions, audit, or MFA proofs.

Memory cache implementation also exists for non-Redis scenarios (Infrastructure DI).

## Related

- [AUTHORIZATION.md](AUTHORIZATION.md)
- [../decisions/ADR-0005-authorization-versions-and-redis.md](../decisions/ADR-0005-authorization-versions-and-redis.md)
