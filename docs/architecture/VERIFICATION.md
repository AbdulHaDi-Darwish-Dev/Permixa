# Verification

## Status

**Implemented** for supported email purposes. Distinct from MFA.

## Domain model

`VerificationChallenge` plus enums:

- `VerificationPurpose`
- `VerificationMethod` (e.g. OTP / URL token as implemented)
- `VerificationChannel` (email implemented; SMS deferred)

Secrets for Identity token providers are **not** stored as Domain `SecretHash` (removed in Identity alignment). Challenges orchestrate lifecycle; Identity issues/validates provider tokens.

## Lifecycle (current)

Implemented behaviors include issue, cooldown/reissue rules, expiration, attempt limits, consumption/invalidation. Exact constants live in Application/Infrastructure services — verify before changing.

Open-challenge uniqueness is enforced via migration `20260910170709_AddOpenVerificationChallengeUniqueIndex`.

## Email confirmation vs email change

- Email confirmation flows use verification challenges + dispatcher.
- Email change uses `PendingEmail` on the user + email-change purpose challenges (see [CREDENTIAL-SECURITY.md](CREDENTIAL-SECURITY.md)).

## Anti-enumeration

Where use cases intentionally avoid revealing whether an email/user exists, treat that as security-sensitive behavior. Do not “improve” messages to be more specific without approval.

## Not MFA

Verification challenges are **not** the MFA login second factor. MFA uses `MfaLoginChallenge` (see [MFA.md](MFA.md)).

## Related

- [EMAIL-DELIVERY.md](EMAIL-DELIVERY.md)
- [CREDENTIAL-SECURITY.md](CREDENTIAL-SECURITY.md)
