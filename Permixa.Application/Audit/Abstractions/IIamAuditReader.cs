using Permixa.Application.Common.Paging;

namespace Permixa.Application.Audit.Abstractions;

public interface IIamAuditReader
{
    Task<PagedResult<IamAuditLogRecord>> SearchAsync(
        IamAuditSearchQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record IamAuditSearchQuery(
    PageRequest Page,
    Guid? ActorUserId = null,
    Guid? TargetUserId = null,
    string? EventType = null,
    IamAuditOutcome? Outcome = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

public sealed record IamAuditLogRecord(
    Guid Id,
    DateTime OccurredAtUtc,
    string EventType,
    IamAuditOutcome Outcome,
    Guid? ActorUserId,
    Guid? TargetUserId,
    Guid? TargetRoleId,
    Guid? TargetPermissionId,
    string? CorrelationId,
    IReadOnlyDictionary<string, string>? Metadata);
