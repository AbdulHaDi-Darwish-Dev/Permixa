# Identity

## Status

**Implemented.** ASP.NET Core Identity is the foundation for users, roles, and credential primitives.

## Historical decision

**Verified historical decision (Phase 1.1 Identity alignment):** Domain must **not** recreate User / Role / UserRole. Early Domain entities for those concepts were removed. Infrastructure owns Identity types.

## Infrastructure types

Implemented in `Permixa.Infrastructure/Identity/`:

- `ApplicationUser : IdentityUser<Guid>`
  - `AuthorizationVersion` (int, starts at 1; EF concurrency token)
  - `IsDisabled` (bool; admin shutdown, distinct from lockout)
  - `PendingEmail` (string?; current email remains until change confirmed)
- `ApplicationRole : IdentityRole<Guid>`
  - `RoleLevel` (int; ≥ 1; smaller = stronger authority)
- `ApplicationDbContext : IdentityDbContext<...>` — hosts Identity stores plus Permixa entities

## What Permixa delegates to Identity

As designed in project prompts and implemented via adapters:

- Password hashing and validation
- Security stamp
- Lockout primitives
- Email confirmation state
- Phone confirmation state (Identity field exists; SMS flows deferred)
- 2FA / authenticator / recovery-code primitives
- Identity token providers
- `UserManager` / `RoleManager` (Infrastructure only)

Permixa adds orchestration and policy **around** Identity rather than reimplementing those primitives.

## Application boundary

Application depends on abstractions such as:

- `IIdentityUserReader` / writers as needed
- `IIdentityAuthenticator`
- `IIdentityPasswordChange`
- MFA-related Identity abstractions

Concrete Identity types must not appear in Domain or Application public contracts.

## Bootstrap Owner email confirmation

**Verified decision (Phase H follow-up, ADR-0012):** Bootstrap-created Owner identities are administratively trusted and are created with `EmailConfirmed=true`. This does not change confirmation requirements for ordinary users.

Idempotent Bootstrap does **not** repair an already-existing Owner’s `EmailConfirmed` flag.

Implemented in: `Permixa.Infrastructure/Bootstrap/PermixaBootstrapper.cs`  
See: [ADR-0012](../decisions/ADR-0012-bootstrap-owner-email-confirmed.md)

## Related

- [AUTHENTICATION.md](AUTHENTICATION.md)
- [ROLE-HIERARCHY.md](ROLE-HIERARCHY.md)
- [../decisions/ADR-0001-aspnet-core-identity.md](../decisions/ADR-0001-aspnet-core-identity.md)
