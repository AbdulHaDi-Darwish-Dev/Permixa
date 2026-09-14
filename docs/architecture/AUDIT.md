# IAM Audit (Phase G)

## Status

**Implemented.**

## Architecture

```text
Application use cases
    → IIamAuditSink.WriteAsync(IamAuditEvent)
        → default SqlIamAuditSink (stages IamAuditLog on DbContext)
    → IIamAuditReader / GetIamAuditLogsUseCase (paged admin read)
```

Application does **not** depend on DbContext, EF, Serilog, HTTP, or SQL-specific types for auditing.

## Default SQL sink

- `SqlIamAuditSink` adds an append-only row; **does not** call `SaveChanges`.
- Participates in ambient `IUnitOfWork` / transaction when the mutation uses one.
- If SQL audit persistence fails inside that transaction → **rollback** (fail closed).
- Hosts replace via DI: `TryAddScoped<IIamAuditSink, SqlIamAuditSink>()` so a prior registration wins.

Custom/external sinks (SIEM, Serilog, OTel): host owns delivery semantics. **No** outbox/Kafka/retry daemon in Phase G. No distributed transactions.

## Persisted model

Table `IamAuditLogs` (migration `20260914120000_AddIamAuditLogs`):

`Id`, `OccurredAtUtc` (datetime2), `EventType`, `Outcome`, `ActorUserId?`, `TargetUserId?`, `TargetRoleId?`, `TargetPermissionId?`, `CorrelationId?`, `MetadataJson?`

- App metadata max **4096** characters; SQL column `nvarchar(max)` (SQL Server length constraint practicality).
- **No foreign keys** to users/roles/permissions (history survives deletion). `ApplicationDbContext` strips inferred FKs.
- Indexes: OccurredAtUtc+Id, ActorUserId, TargetUserId, EventType.

## Catalog and outcome

- `IamAuditEvents` constants (Users, Roles, Permissions, Sessions, Credentials, Mfa, …).
- `IamAuditOutcome`: Success | Failure.
- Authentication login volume events **deferred** (not implemented).

## Actor / target / metadata / PII

- Admin: `ActorUserId` = acting admin; targets as typed Guids.
- Self-service: actor = target is acceptable.
- Prefer IDs over copying PII. Phase G does **not** store emails in metadata.
- CorrelationId: always null in Phase G (no Application correlation abstraction).
- Never store secrets in metadata (passwords, tokens, MFA material, etc.).

## Read API

- `GetIamAuditLogsUseCase` requires `Iam.Audit.Read`.
- Paging mandatory (`PageRequest` / `PagedResult`).
- Filters: actor, target user, event type, outcome, from/to UTC.
- Order: `OccurredAtUtc DESC`, `Id DESC`.
- **No** hierarchy filtering for audit readers (security/compliance admin policy).
- Query shape: count + page (bounded; no N+1 name resolution).

## Retention / tamper resistance

- No automatic retention job (host policy). Table grows over time.
- No cryptographic hash chaining in Phase G. Append-only via Application APIs only.

## Authorization versions

Audit insert itself must **never** increment `AuthorizationVersion` / `RbacVersion`.

## Known tradeoffs

Some email-change paths may flush verification-related state before audit flush depending on service boundaries — treat as a known limitation vs verification dispatch; do not silently invent distributed transactions.

## Related

- [../decisions/ADR-0011-sql-iam-audit-sink.md](../decisions/ADR-0011-sql-iam-audit-sink.md)
- Tests: `IamAuditTests`, `IamAuditFlowTests`, Application audit tests
