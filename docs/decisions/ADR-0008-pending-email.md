# ADR-0008 — PendingEmail for email change

- **Status:** Accepted

## Context

Email change must not swap the live email before verification.

## Decision

Store unverified target in `ApplicationUser.PendingEmail`. Keep current email active until `ConfirmEmailChange` succeeds. Invalidate prior open email-change challenges when pending changes (as implemented).

## Verified rationale

Phase E credentials/email change design and migration `AddUserPendingEmail`.

## Consequences

PendingEmail is PII at rest on the user row. Audit deliberately prefers user IDs over copying emails into audit metadata (Phase G).

## Alternatives considered

No historically verified alternatives recorded.

## Source/evidence note

`ApplicationUser.PendingEmail`; `PendingEmailChangeService`; Phase E prompts.
