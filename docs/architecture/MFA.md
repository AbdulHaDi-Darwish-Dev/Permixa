# MFA (Phase F)

## Status

**Implemented — MFA v1.**

## Scope included

- Optional per-user MFA
- Authenticator TOTP (Identity primitives)
- Recovery codes
- Setup / enable / disable / regenerate recovery codes
- Login password step → MFA pending challenge when 2FA enabled
- MFA completion (TOTP or recovery code) → tokens

## Login API (Option A — verified)

```text
LoginUseCase → Result<LoginResult>
  Authenticated(AuthenticationResult)   // tokens only when fully authenticated
  MfaRequired(MfaProof, ExpiresAtUtc)   // success discriminant, not Result.Failure
```

`AuthenticationResult` continues to mean fully authenticated credentials only.

Password verification alone for MFA-enabled users **never** issues JWT/refresh tokens.

## MfaLoginChallenge

Domain entity `MfaLoginChallenge`:

- One row per user; new password login **replaces** challenge in place
- Stores **proof hash only** (raw proof never persisted)
- Lifetime, attempt limit, single-use consume semantics
- Concurrency: unique UserId + proof-hash concurrency token + consume via set-based update (as implemented)

Options: `PermixaMfaOptions` (defaults include challenge lifetime 5 minutes, max attempts 5, recovery code count 10, issuer `"Permixa"`).

## Session revocation

MFA enable/disable (and related security mutations) revoke sessions as implemented in those use cases. Verify exact revoke scope in code before changing.

## Explicitly excluded from Phase F

```text
SMS MFA
Email OTP as MFA
Passkeys / WebAuthn
Remember device / trusted devices
Device metadata
Admin MFA reset (beyond documented flows)
```

## Audit

Events: `Mfa.Enabled`, `Mfa.Disabled`, `Mfa.RecoveryCodesRegenerated`. Never audit secrets/codes/proofs.

## Related

- [AUTHENTICATION.md](AUTHENTICATION.md)
- [../decisions/ADR-0009-login-result-mfa.md](../decisions/ADR-0009-login-result-mfa.md)
- [../decisions/ADR-0010-mfa-login-challenge.md](../decisions/ADR-0010-mfa-login-challenge.md)
- Migration: `20260913220000_AddMfaLoginChallenges`
