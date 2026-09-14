# Phase F — MFA v1

## Goal

Self-service authenticator MFA + login enforcement (not SMS/passkeys).

## Important decisions

- Login API Option A: `Result<LoginResult>` with `Authenticated` | `MfaRequired`
- Dedicated `MfaLoginChallenge` (proof hash, one row/user)
- No tokens until MFA completion
- Session revoke on enable/disable as implemented

## Migration

`20260913220000_AddMfaLoginChallenges`

## Excluded

SMS MFA, passkeys, remember device, device metadata, admin MFA reset expansion.

## Verified baseline after F (historical report)

```text
Domain 33 | Application 217 | Infrastructure 170 | AspNetCore 29 | Integration 50
Total 499 / 499
```

Superseded by Phase G baseline in [CURRENT-STATE.md](../CURRENT-STATE.md).

## Follow-up

Phase G — IAM Audit.
