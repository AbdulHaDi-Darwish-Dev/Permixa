using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Common.Paging;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Audit;

public sealed class SqlIamAuditReader : IIamAuditReader
{
    private readonly ApplicationDbContext _db;

    public SqlIamAuditReader(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<IamAuditLogRecord>> SearchAsync(
        IamAuditSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = query.Page;
        IQueryable<Domain.Audit.IamAuditLog> filtered = _db.IamAuditLogs.AsNoTracking();

        if (query.ActorUserId is Guid actor)
            filtered = filtered.Where(l => l.ActorUserId == actor);

        if (query.TargetUserId is Guid target)
            filtered = filtered.Where(l => l.TargetUserId == target);

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            var eventType = query.EventType.Trim();
            filtered = filtered.Where(l => l.EventType == eventType);
        }

        if (query.Outcome is IamAuditOutcome outcome)
        {
            var persisted = IamAuditMetadataSerializer.ToPersistedOutcome(outcome);
            filtered = filtered.Where(l => l.Outcome == persisted);
        }

        if (query.FromUtc is DateTime fromUtc)
            filtered = filtered.Where(l => l.OccurredAtUtc >= fromUtc);

        if (query.ToUtc is DateTime toUtc)
            filtered = filtered.Where(l => l.OccurredAtUtc <= toUtc);

        var total = await filtered.CountAsync(cancellationToken);

        var rows = await filtered
            .OrderByDescending(l => l.OccurredAtUtc)
            .ThenByDescending(l => l.Id)
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(l => new IamAuditLogRecord(
                l.Id,
                l.OccurredAtUtc,
                l.EventType,
                IamAuditMetadataSerializer.ParseOutcome(l.Outcome),
                l.ActorUserId,
                l.TargetUserId,
                l.TargetRoleId,
                l.TargetPermissionId,
                l.CorrelationId,
                IamAuditMetadataSerializer.Deserialize(l.MetadataJson)))
            .ToArray();

        return new PagedResult<IamAuditLogRecord>(items, page.Page, page.PageSize, total);
    }
}
