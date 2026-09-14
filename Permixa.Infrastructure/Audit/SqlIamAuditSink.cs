using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Domain.Audit;
using Permixa.Infrastructure.Persistence;

namespace Permixa.Infrastructure.Audit;

/// <summary>
/// Default SQL audit sink. Stages an append-only row on the current DbContext.
/// Does not call SaveChanges; participates in the ambient unit-of-work / transaction.
/// </summary>
public sealed class SqlIamAuditSink : IIamAuditSink
{
    private readonly ApplicationDbContext _db;

    public SqlIamAuditSink(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task WriteAsync(
        IamAuditEvent auditEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        cancellationToken.ThrowIfCancellationRequested();

        var metadataJson = IamAuditMetadataSerializer.Serialize(auditEvent.Metadata);
        var log = IamAuditLog.Create(
            auditEvent.EventType,
            IamAuditMetadataSerializer.ToPersistedOutcome(auditEvent.Outcome),
            auditEvent.OccurredAtUtc,
            auditEvent.ActorUserId,
            auditEvent.TargetUserId,
            auditEvent.TargetRoleId,
            auditEvent.TargetPermissionId,
            auditEvent.CorrelationId,
            metadataJson);

        _db.IamAuditLogs.Add(log);
        return Task.CompletedTask;
    }
}
