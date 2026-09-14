using Permixa.Domain.Common;

namespace Permixa.Domain.Audit;

/// <summary>
/// Append-only IAM audit record. Stores Guid identifiers without FK delete coupling.
/// </summary>
public sealed class IamAuditLog
{
    public const int MaxEventTypeLength = 128;
    public const int MaxOutcomeLength = 16;
    public const int MaxCorrelationIdLength = 64;
    public const int MaxMetadataJsonLength = 4096;

    private IamAuditLog(
        Guid id,
        DateTime occurredAtUtc,
        string eventType,
        string outcome,
        Guid? actorUserId,
        Guid? targetUserId,
        Guid? targetRoleId,
        Guid? targetPermissionId,
        string? correlationId,
        string? metadataJson)
    {
        Id = id;
        OccurredAtUtc = occurredAtUtc;
        EventType = eventType;
        Outcome = outcome;
        ActorUserId = actorUserId;
        TargetUserId = targetUserId;
        TargetRoleId = targetRoleId;
        TargetPermissionId = targetPermissionId;
        CorrelationId = correlationId;
        MetadataJson = metadataJson;
    }

    public Guid Id { get; }

    public DateTime OccurredAtUtc { get; }

    public string EventType { get; }

    public string Outcome { get; }

    public Guid? ActorUserId { get; }

    public Guid? TargetUserId { get; }

    public Guid? TargetRoleId { get; }

    public Guid? TargetPermissionId { get; }

    public string? CorrelationId { get; }

    public string? MetadataJson { get; }

    public static IamAuditLog Create(
        string eventType,
        string outcome,
        DateTime occurredAtUtc,
        Guid? actorUserId = null,
        Guid? targetUserId = null,
        Guid? targetRoleId = null,
        Guid? targetPermissionId = null,
        string? correlationId = null,
        string? metadataJson = null,
        Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new DomainException("Audit event type cannot be empty.");

        if (eventType.Length > MaxEventTypeLength)
            throw new DomainException($"Audit event type cannot exceed {MaxEventTypeLength} characters.");

        if (string.IsNullOrWhiteSpace(outcome))
            throw new DomainException("Audit outcome cannot be empty.");

        if (outcome.Length > MaxOutcomeLength)
            throw new DomainException($"Audit outcome cannot exceed {MaxOutcomeLength} characters.");

        if (correlationId is { Length: > MaxCorrelationIdLength })
            throw new DomainException($"Audit correlation id cannot exceed {MaxCorrelationIdLength} characters.");

        if (metadataJson is { Length: > MaxMetadataJsonLength })
            throw new DomainException($"Audit metadata cannot exceed {MaxMetadataJsonLength} characters.");

        return new IamAuditLog(
            id ?? Guid.NewGuid(),
            occurredAtUtc,
            eventType.Trim(),
            outcome.Trim(),
            actorUserId,
            targetUserId,
            targetRoleId,
            targetPermissionId,
            string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim(),
            string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson);
    }

    public static IamAuditLog Reconstitute(
        Guid id,
        DateTime occurredAtUtc,
        string eventType,
        string outcome,
        Guid? actorUserId,
        Guid? targetUserId,
        Guid? targetRoleId,
        Guid? targetPermissionId,
        string? correlationId,
        string? metadataJson)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Audit id cannot be empty.", nameof(id));

        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);

        return new IamAuditLog(
            id,
            occurredAtUtc,
            eventType,
            outcome,
            actorUserId,
            targetUserId,
            targetRoleId,
            targetPermissionId,
            correlationId,
            metadataJson);
    }
}
