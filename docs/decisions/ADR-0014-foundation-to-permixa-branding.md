# ADR-0014 — Foundation → Permixa pre-public branding migration

- **Status:** Accepted
- **Date context:** Phase I-1 (2026-09-14)

## Context

The product was developed under the internal name **Foundation**. Before any public NuGet package shipped, the product brand was fixed as **Permixa**.

No external compatibility guarantees existed for published packages.

## Decision

1. Rename projects, namespaces, assemblies, and product-branded public CLR APIs from `Foundation*` / `Foundation.*` to `Permixa*` / `Permixa.*`.
2. Rename host registration methods (`AddFoundation*` → `AddPermixa*`).
3. Do **not** create obsolete aliases, dual APIs, or `Foundation.*` shim packages.
4. Preserve persisted business/security identifiers: `Iam.*` permissions, JWT claims, audit `EventType` values, verification semantics, migration IDs, SQL schema.
5. Update disposable/technical namespaces where branding is useful: Redis `permixa:authz:`, Resend idempotency `permixa-verification/`, ASP.NET policy prefix `Permixa.Permission:`, design-time env `PERMIXA_CONNECTION_STRING`.
6. Prefer **brand-neutral** machine-readable ProblemDetails codes:
   - `InternalError`
   - `Concurrency.Conflict`
   - `Email.DeliveryFailed`
   - (leave `Authentication.Unauthorized`, `Authorization.Forbidden`, `RateLimiting.TooManyRequests`)
7. Update EF model snapshot / Designer CLR FQNs to `Permixa.*` without creating a new migration or altering `__EFMigrationsHistory`.

## Consequences

Source and public API identity match the product brand. Historical ADRs/phase docs may still mention Foundation as the prior internal name. Packaging remains a separate approved phase.

## Alternatives considered

- Ship first packages as `Foundation.*` then rename later — rejected (no public ship yet; rename now is cheaper)
- Dual Foundation/Permixa APIs — rejected (clean pre-1.0 surface)
- Rename `Iam.*` / JWT claims / audit events for branding — rejected (persisted security identifiers)

## Source/evidence note

Owner-approved Phase I-1 decisions (Redis A, idempotency A, error codes C, policy prefix A, env var A, EF snapshot FQNs A).
