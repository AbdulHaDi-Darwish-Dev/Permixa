# ADR-0001 — Use ASP.NET Core Identity for users/roles/passwords

- **Status:** Accepted
- **Date context:** Phase 1.1 Identity alignment (2026-09-08)

## Context

Early Domain included User/Role/UserRole entities. The project required reusable IAM without reinventing Identity primitives.

## Decision

ASP.NET Core Identity owns Users, Roles, UserRoles, password hashing, security stamps, lockout, email/phone confirmation, 2FA primitives, token providers, UserManager/RoleManager. Domain/Application must not depend on concrete Identity types. Infrastructure hosts `ApplicationUser` / `ApplicationRole`.

## Verified rationale

Explicit correction prompt: build on Identity rather than recreating Identity concepts. Domain User/Role/UserRole were removed.

## Consequences

- Permixa adds permissions, hierarchy, JWT refresh, verification orchestration, MFA login challenge, audit.
- Identity types stay in Infrastructure.

## Alternatives considered

Custom Domain User/Role model (initially implemented, then rejected by alignment correction).

## Source/evidence note

Phase 1.1 user correction prompt and subsequent Domain cleanup report; current `Permixa.Infrastructure/Identity/*`.
