# Email delivery

## Status

**Implemented.**

## Abstraction

Application defines verification/email dispatch abstractions (e.g. `IVerificationDispatcher`, outgoing message types). Infrastructure provides:

- `EmailVerificationDispatcher` — selects templates by purpose/method/channel, logs **ChallengeId** (not secrets), sends via `IEmailSender`
- `ResendEmailSender` — Resend provider implementation
- Embedded HTML/TXT templates under `Permixa.Infrastructure/Email/Templates/` (embedded resources)

## Behavior notes (implementation fact)

- Idempotency key strategy exists for provider sends (see dispatcher).
- Provider failures map to `EmailDeliveryException`; AspNetCore exception handler maps to HTTP 503 for hosts using Permixa exception handling.
- Logs use provider message id / error type / status code — not raw tokens or passwords (Phase G logging review).

## Link templates

Template content includes placeholders for links/OTP as designed per purpose. Do not put live secrets into documentation or tests beyond test fixtures.

## Deferred

SMS channel and non-Resend providers remain host/extension territory unless a future phase approves them.

## Related

- [VERIFICATION.md](VERIFICATION.md)
