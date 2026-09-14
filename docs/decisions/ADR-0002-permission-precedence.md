# ADR-0002 — Permission precedence and default deny

- **Status:** Accepted

## Context

Need deterministic effective-permission resolution with role grants and user overrides.

## Decision

Precedence: **User Deny > User Allow > Role grant > Default Deny**. Absence of override means inherit roles.

Permissions answer WHAT; hierarchy answers WHO (separate concern).

## Verified rationale

Documented on `PermissionAuthorizationResolver` and reinforced in Application authorization design prompts.

## Consequences

Deny always wins over Allow. Default is secure closed. Allow override cannot replace hierarchy checks.

## Alternatives considered

No historically verified alternatives recorded beyond inherit-via-null-override.

## Source/evidence note

`Permixa.Domain/Authorization/PermissionAuthorizationResolver.cs`; Application authorization phase prompts.
