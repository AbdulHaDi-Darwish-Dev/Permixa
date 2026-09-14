# Authorization

## Status

**Implemented.**

## Core concepts

| Concept | Location | Role |
|---------|----------|------|
| `Permission` | Domain | Named capability (`Resource.Action` style validated in Application) |
| `RolePermission` | Domain | Role grant |
| `UserPermissionOverride` | Domain | Per-user Allow/Deny |
| `PermissionEffect` | Domain | `Allow` / `Deny` (absence = inherit) |
| `AuthorizationState` | Domain | Singleton row holding `RbacVersion` |
| `PermissionAuthorizationResolver` | Domain | Pure precedence |
| `EffectivePermissionService` | Application | Orchestrates load + resolve + cache |
| `IamPermissions` | Application | Framework IAM admin permission catalog (19 entries including `Iam.Audit.Read`) |

## Precedence (verified in code)

```text
User Deny
  > User Allow
  > Role grant
  > Default Deny
```

Source: `PermissionAuthorizationResolver` XML docs and implementation.

## WHAT vs WHO

**Verified historical decision:**

- **Permissions** answer: *What may this actor do?*
- **Role hierarchy** answers: *Who may this actor affect?*

User override **Allow** does **not** bypass hierarchy gates on administrative operations that call `IAuthorizationHierarchyService.CanManageUserAsync` / role control APIs.

## Effective permissions

`EffectivePermissionService` aggregates role grants and overrides, applies resolver rules, and uses `IPermissionCache` with versioned `AuthorizationSnapshot` (see [AUTHORIZATION-VERSIONING-AND-CACHE.md](AUTHORIZATION-VERSIONING-AND-CACHE.md)).

## IAM permissions

Consuming apps define business permissions separately. Permixa seeds `IamPermissions.All` for IAM administration (permissions, roles, users, sessions, audit read, etc.).

There is **no** `Iam.Audit.Write` — audit writes are internal framework behavior.

## Related

- [ROLE-HIERARCHY.md](ROLE-HIERARCHY.md)
- [AUTHORIZATION-VERSIONING-AND-CACHE.md](AUTHORIZATION-VERSIONING-AND-CACHE.md)
- [../decisions/ADR-0002-permission-precedence.md](../decisions/ADR-0002-permission-precedence.md)
