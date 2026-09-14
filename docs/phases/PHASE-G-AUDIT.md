# Phase G — IAM Audit

## Goal

Reusable IAM audit: Application `IIamAuditSink` + default SQL implementation + admin paged read.

## Important decisions

- Audit ≠ application logging
- Stage-only SQL sink; ambient transaction; fail closed
- Replaceable via `TryAddScoped`
- No FKs; append-only; `Iam.Audit.Read`
- Login authentication audit deferred
- No emails in audit metadata; CorrelationId null
- Metadata app max 4096; column nvarchar(max)

## Migration

`20260914120000_AddIamAuditLogs`

## Instrumented

Admin/self security mutations (permissions, roles, overrides, users, sessions, credentials/email, MFA). Logout via token service.

## Tests

Infrastructure `IamAuditTests`, Integration `IamAuditFlowTests`, Application/Domain audit tests, `SensitiveLoggingReviewTests`.

## Verified baseline after G

```text
Domain 36 | Application 224 | Infrastructure 183 | AspNetCore 29 | Integration 51
Total 523 / 523 | 0 errors | 0 warnings | 0 skips
```

(Re-verified 2026-09-14 during documentation task.)

## Follow-up

No next implementation phase approved. Packaging remains paused.
