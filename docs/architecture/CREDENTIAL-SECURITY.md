# Credential security

## Status

**Implemented** (Phase E and related).

## Capabilities

| Use case | Notes |
|----------|--------|
| `ChangePasswordUseCase` | Verifies current password; revokes refresh families (optionally preserves current family if provided and active); audits `Password.Changed` |
| `RequestEmailChangeUseCase` | Self-service pending email + verification |
| `ConfirmEmailChangeUseCase` | Confirms pending email via challenge |
| `AdminRequestEmailChangeUseCase` | Admin-initiated pending email change |
| `ForcePasswordResetUseCase` | Admin force reset path; session revoke + audit `PasswordReset.Forced` |

## PendingEmail design

**Implemented:**

- `ApplicationUser.PendingEmail` holds the unverified target address.
- Current email remains active until confirmation.
- Changing pending email invalidates open email-change challenges as implemented in `PendingEmailChangeService`.

Uniqueness/concurrency guarantees: document only what tests and code enforce. Do not invent stronger global uniqueness than implemented — inspect `CredentialsAndEmailChangeTests` and configurations before claiming uniqueness semantics.

## Session revocation tradeoff

Password change / force reset / disable-style flows revoke refresh families. **Access JWTs are not blacklisted** (short TTL tradeoff).

## Secrets

Never log or audit passwords, verification tokens, or pending-email tokens. Phase G audit stores IDs for email-change events; **emails are not stored in audit metadata** (privacy stance taken in Phase G).

## Related

- [VERIFICATION.md](VERIFICATION.md)
- [SESSIONS.md](SESSIONS.md)
- [AUDIT.md](AUDIT.md)
