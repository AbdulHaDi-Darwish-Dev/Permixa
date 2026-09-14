using Permixa.Domain.Audit;

namespace Permixa.Application.Audit;

/// <summary>
/// Application-level audit event. Free of EF/SQL/HTTP types.
/// </summary>
public sealed class IamAuditEvent
{
    public IamAuditEvent(
        string eventType,
        IamAuditOutcome outcome,
        DateTime occurredAtUtc,
        Guid? actorUserId = null,
        Guid? targetUserId = null,
        Guid? targetRoleId = null,
        Guid? targetPermissionId = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));

        if (eventType.Length > IamAuditLog.MaxEventTypeLength)
            throw new ArgumentException(
                $"Event type cannot exceed {IamAuditLog.MaxEventTypeLength} characters.",
                nameof(eventType));

        if (correlationId is { Length: > IamAuditLog.MaxCorrelationIdLength })
            throw new ArgumentException(
                $"Correlation id cannot exceed {IamAuditLog.MaxCorrelationIdLength} characters.",
                nameof(correlationId));

        EventType = eventType.Trim();
        Outcome = outcome;
        OccurredAtUtc = occurredAtUtc;
        ActorUserId = actorUserId;
        TargetUserId = targetUserId;
        TargetRoleId = targetRoleId;
        TargetPermissionId = targetPermissionId;
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim();
        Metadata = metadata;
    }

    public string EventType { get; }

    public IamAuditOutcome Outcome { get; }

    public DateTime OccurredAtUtc { get; }

    public Guid? ActorUserId { get; }

    public Guid? TargetUserId { get; }

    public Guid? TargetRoleId { get; }

    public Guid? TargetPermissionId { get; }

    public string? CorrelationId { get; }

    public IReadOnlyDictionary<string, string>? Metadata { get; }

    public static IamAuditEvent Success(
        string eventType,
        DateTime occurredAtUtc,
        Guid? actorUserId = null,
        Guid? targetUserId = null,
        Guid? targetRoleId = null,
        Guid? targetPermissionId = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            eventType,
            IamAuditOutcome.Success,
            occurredAtUtc,
            actorUserId,
            targetUserId,
            targetRoleId,
            targetPermissionId,
            correlationId,
            metadata);

    public static IamAuditEvent Failure(
        string eventType,
        DateTime occurredAtUtc,
        Guid? actorUserId = null,
        Guid? targetUserId = null,
        Guid? targetRoleId = null,
        Guid? targetPermissionId = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            eventType,
            IamAuditOutcome.Failure,
            occurredAtUtc,
            actorUserId,
            targetUserId,
            targetRoleId,
            targetPermissionId,
            correlationId,
            metadata);
}
