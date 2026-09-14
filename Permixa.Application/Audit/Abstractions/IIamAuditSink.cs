namespace Permixa.Application.Audit.Abstractions;

/// <summary>
/// Replaceable IAM audit sink. Default SQL implementation participates in the ambient unit of work.
/// Custom sinks (SIEM/Serilog/OTel) own their own delivery semantics and are not enlisted in SQL transactions.
/// </summary>
public interface IIamAuditSink
{
    /// <summary>
    /// Stages or delivers an audit event. The default SQL sink adds a row to the current DbContext
    /// and does not call SaveChanges; callers must flush via the existing unit of work.
    /// </summary>
    Task WriteAsync(
        IamAuditEvent auditEvent,
        CancellationToken cancellationToken = default);
}
