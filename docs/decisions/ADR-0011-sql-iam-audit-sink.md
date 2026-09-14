# ADR-0011 — Replaceable IIamAuditSink + default SQL

- **Status:** Accepted (Phase G)

## Context

Need production IAM audit without coupling use cases to EF/Serilog/HTTP, while allowing hosts to replace delivery.

## Decision

- Application emits via `IIamAuditSink`
- Default `SqlIamAuditSink` stages rows on ambient DbContext (no hidden SaveChanges)
- Participate in IAM transactions; fail closed on SQL audit failure
- Register with `TryAddScoped` so hosts can override
- No composite sink/outbox/Kafka in Phase G
- Append-only `IamAuditLogs` without FKs to IAM entities
- Admin read via `GetIamAuditLogsUseCase` + `Iam.Audit.Read`

## Verified rationale

Phase G approved prompt: replaceable sink + default SQL; atomicity goal for mutations; fail closed; no distributed TX with external SIEM.

## Consequences

External sinks do not share SQL transactions. Login authentication audit deferred. CorrelationId null until Application-safe source exists.

## Alternatives considered

Enterprise generic event store / outbox explicitly out of Phase G scope per prompt.

## Source/evidence note

Phase G prompt + final report; `IIamAuditSink`, `SqlIamAuditSink`, migration `AddIamAuditLogs`.
