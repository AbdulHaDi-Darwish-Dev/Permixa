# Phase H — Disposable Consumer & Developer Experience Validation

## Goal

Validate Permixa from a real external ASP.NET Core host perspective without adding a permanent sample product.

## Method

- Created a **temporary** Web API under `artifacts/consumer-validation/`
- Referenced **only** `Permixa.AspNetCore` (Application + Infrastructure transitive)
- Used public DI registration + use cases + AspNetCore authz helpers
- SQL via Testcontainers; **memory** permission cache (Redis not required)
- Host-owned `IVerificationDispatcher` capture (no Resend / no real email)
- Scripted HTTP flows against temporary endpoints
- **Deleted** the disposable host completely after validation

> Disposable consumer was created for validation and deleted afterward.  
> It is **not** part of the Permixa product. No sample source remains in the repository.

## Registration sequence used

```text
AddPermixaInfrastructure(...)   // SQL + bootstrap options + Identity/EF
AddPermixaAuthentication(...)   // JWT private PEM + MFA options
AddPermixaVerification()
// host: AddSingleton<IVerificationDispatcher>(capturingFake)
// NOT AddPermixaResendEmail
AddPermixaJwtBearer(...)        // public PEM
AddPermixaPermissionAuthorization()
AddPermixaProblemDetails()
AddPermixaAuthorization()       // admin/session/audit use cases
```

Then host:

```text
ApplicationDbContext.Database.MigrateAsync()
IPermixaBootstrapper.BootstrapAsync()  // explicit, twice for idempotence
UseExceptionHandler / UseAuthentication / UseAuthorization
```

**Future recommendation (not approved):** a unified `AddPermixa()` facade. Do not invent it without approval.

## Configuration required

| Area | Required |
|------|----------|
| SQL Server connection string | Yes (fail-fast if missing) |
| Bootstrap Owner email/username/password when Enabled | Yes |
| JWT Issuer, Audience, PrivateKeyPem | Yes (auth registration fail-fast) |
| JWT PublicKeyPem (AspNetCore bearer) | Yes |
| Resend | Optional if host supplies `IVerificationDispatcher` |
| Redis | Optional (memory cache default) |
| MFA options | Defaults OK |

## Flows validated (PASS)

- Bootstrap + migrate + idempotent bootstrap
- Register, email confirmation request/confirm via fake dispatcher
- Login / Refresh / Logout
- `LoginResult`: Authenticated vs MfaRequired (MFA path)
- `RequirePermission` 403 vs authorized Owner
- CreateRole / ChangeRolePosition / AssignRoleToUser
- CreatePermission / AssignPermissionToRole / SetUserPermissionOverride
- GetUserIamDetails / Lock / Unlock / Disable / Enable
- GetMySessions / RevokeMySession / RevokeAllMySessions
- ChangePassword / RequestEmailChange / ConfirmEmailChange
- MFA begin setup / enable / login MFA / TOTP complete / recovery complete / disable
- GetIamAuditLogs + deny without `Iam.Audit.Read`
- ProblemDetails 400/404/409; unauthenticated 401
- SQL + JWT fail-fast at registration

## Public APIs actually required

| API | Classification |
|-----|----------------|
| `AddPermixaInfrastructure` / Authentication / Verification / Authorization | Good |
| `AddPermixaJwtBearer` / `RequirePermission` / `ToHttpResult` / `ICurrentUser` | Good |
| Use cases (Login, Register, MFA, admin, sessions, audit, verification) | Good |
| Host `IVerificationDispatcher` replacement (skip Resend) | Good |
| `IPermixaBootstrapper` explicit invoke | Good |
| `ApplicationDbContext.MigrateAsync` | Acceptable (Infrastructure type; no dedicated migrator API) |
| `GetMyRoles` to obtain Owner `referenceRoleId` | Acceptable (documented workaround) |
| `GetRoles` omitting Owner | Awkward (see below) |
| `AuthenticationResult` without `FamilyId` | Awkward (list sessions to revoke) |
| Bootstrap Owner + `RequireConfirmedEmail=true` | **Resolved** (Option A — see follow-up below) |

## Developer-experience strengths

- Single project reference to `Permixa.AspNetCore` is enough to compile against use cases.
- Module registration names are discoverable; JWT/SQL fail fast with clear messages.
- Fake verification dispatcher works without Resend.
- Memory cache path works without Redis.
- `LoginResult` discriminants are clear for hosts (`IsAuthenticated` / `IsMfaRequired`).
- `RequirePermission` integrates cleanly with Minimal APIs.
- Result → ProblemDetails helpers reduce host error mapping.
- MFA status does not re-expose shared key; recovery codes only at enable.

## Friction / issues

### Blocking issue — resolved (Option A)

**Originally observed:** Bootstrap Owner created with `EmailConfirmed=false`, so `RequireConfirmedEmail=true` prevented Owner login without internals.

**Approved fix (Option A):** newly created Bootstrap Owner is created with `EmailConfirmed=true`. Ordinary users unchanged. Existing Owners are **not** repaired on idempotent Bootstrap.

Documented in [ADR-0012](../decisions/ADR-0012-bootstrap-owner-email-confirmed.md).

**Regression (2026-09-14):** temporary consumer re-validated `RequireConfirmedEmail=true` → Bootstrap → Owner login succeeds; disposable host deleted again.

### Non-blocking / Awkward

1. **`GetRoles` hides Owner** (hierarchy `CanControl` filter). Consumers creating roles Below Owner must use **`GetMyRoles`** for `referenceRoleId`. Document for hosts; intentional hierarchy semantics, but easy to miss.
2. **`AuthenticationResult` has no `FamilyId`**. Revoke flows need `GetMySessions` first. Acceptable if documented.
3. **Migration via `ApplicationDbContext`** couples hosts to Infrastructure persistence type. Acceptable until a dedicated migrator helper exists (**future recommendation** only).
4. **Many registration calls** (no unified `AddPermixa()`). Acceptable; unified facade is a future recommendation.
5. **`IEmailSender` lives in Infrastructure**; preferred extension point for no-Resend hosts is Application `IVerificationDispatcher` (works well).
6. Display-formatted MFA `SharedKey` (spaces / lowercase) — hosts must normalize for TOTP libraries. Awkward docs issue, not a security bug.

### Configuration / module registration

- Omitting `IVerificationDispatcher` after `AddPermixaVerification` without Resend → runtime failure when verification dispatches (**report:** prefer startup validation — **future recommendation**, not implemented).
- Registration order: Infrastructure before Authentication/Verification/Authorization as documented in method XML.

## Security observations

- No tokens issued on MFA-required login.
- `RequirePermission` enforced; audit denied without `Iam.Audit.Read`.
- GetMfaStatus does not return shared key.
- Hierarchy/Owner paths exercised through public use cases (no bypass attempted).
- Disposable host did not log passwords, tokens, MFA proofs, shared keys, or verification secrets.

## Permixa production code

Phase H itself made **no** Permixa production changes.  
**Follow-up (approved Option A):** `PermixaBootstrapper` sets `EmailConfirmed=true` for newly created Owners only.

## Packaging readiness

Phase H blocking Owner/`RequireConfirmedEmail` issue is **resolved**. Remaining Phase H items are non-blocking DX friction. Packaging may proceed to inspection when explicitly instructed (still not started).

## Unresolved decisions

1. ~~How should bootstrap interact with `RequireConfirmedEmail`?~~ **Resolved — Option A / ADR-0012**
2. Whether to add `FamilyId` to `AuthenticationResult` (recommendation: optional additive field — needs approval)
3. Whether to add unified `AddPermixa()` (recommendation only)
4. Whether to add startup check for missing `IVerificationDispatcher`

## Baseline

Permixa regression suites re-run after cleanup — see [CURRENT-STATE.md](../CURRENT-STATE.md).
