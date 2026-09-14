# Authentication

## Status

**Implemented** (JWT + refresh + login/MFA orchestration).

## Access tokens

- Generator: `RsaJwtAccessTokenGenerator` / `PermixaJwtOptions` (Infrastructure).
- Algorithm: **RS256** (`SecurityAlgorithms.RsaSha256`).
- Host supplies RSA private key PEM (≥ 2048 bits); AspNetCore validates with public PEM.
- Default access lifetime: **15 minutes** (configurable via options).
- Claims include identity/session identifiers as implemented (`sub`, `jti`, `iat`, etc.).

### JWT claim philosophy

**Verified historical decision:** do **not** put permission lists or `RoleLevel` in the JWT. Authorization is resolved server-side (SQL + cache).

## Refresh tokens

- Domain entity: `RefreshToken` (hash stored; raw token not persisted).
- **FamilyId:** one logical login/session. Rotation keeps the same family; new login creates a new family.
- Replay of a replaced refresh token revokes the family (`RefreshTokenReuseDetected` path in `AuthenticationTokenService`).

## No access-token blacklist

**Verified historical direction:** no access-token blacklist subsystem. Security relies on short-lived access tokens plus refresh/session revocation. Changing this requires approval.

## AuthenticationResult vs LoginResult

**Verified Phase F decision (Option A):**

- `AuthenticationResult` means **fully authenticated** credentials (access + refresh tokens). Unchanged meaning.
- `LoginUseCase` returns `Result<LoginResult>` where success is mutually exclusive:
  - `LoginResult.Authenticated(AuthenticationResult)`
  - `LoginResult.MfaRequired(mfaProof, expiresAtUtc)` — **success state**, not `Result.Failure`
- Password step for MFA-enabled users **must not** issue JWT/refresh tokens.

Implemented in:

- `Permixa.Application/Authentication/Models/LoginResult.cs`
- `Permixa.Application/Authentication/Login/LoginUseCase.cs`

## Related use cases

Login, refresh, logout/revoke via `AuthenticationTokenService`, change password, MFA complete (TOTP/recovery).

## Related

- [SESSIONS.md](SESSIONS.md)
- [MFA.md](MFA.md)
- [../decisions/ADR-0009-login-result-mfa.md](../decisions/ADR-0009-login-result-mfa.md)
