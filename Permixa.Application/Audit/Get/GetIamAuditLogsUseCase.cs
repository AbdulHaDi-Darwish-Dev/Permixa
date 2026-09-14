using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Paging;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Audit.Get;

public sealed record GetIamAuditLogsQuery(
    Guid ActorUserId,
    PageRequest Page,
    Guid? FilterActorUserId = null,
    Guid? FilterTargetUserId = null,
    string? EventType = null,
    IamAuditOutcome? Outcome = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

public sealed record IamAuditLogDto(
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

public sealed class GetIamAuditLogsUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IIamAuditReader _reader;

    public GetIamAuditLogsUseCase(
        IEffectivePermissionService effectivePermissions,
        IIamAuditReader reader)
    {
        _effectivePermissions = effectivePermissions;
        _reader = reader;
    }

    public async Task<Result<PagedResult<IamAuditLogDto>>> ExecuteAsync(
        GetIamAuditLogsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!query.Page.IsValid)
            return Result.Failure<PagedResult<IamAuditLogDto>>(AuthorizationErrors.InvalidPaging);

        if (query.FromUtc is DateTime from
            && query.ToUtc is DateTime to
            && from > to)
        {
            return Result.Failure<PagedResult<IamAuditLogDto>>(AuthorizationErrors.InvalidPaging);
        }

        var permission = await _effectivePermissions.HasPermissionAsync(
            query.ActorUserId,
            IamPermissions.Audit.Read,
            cancellationToken);

        if (permission.IsFailure)
            return Result.Failure<PagedResult<IamAuditLogDto>>(permission.Error!);

        if (!permission.Value)
            return Result.Failure<PagedResult<IamAuditLogDto>>(AuthorizationErrors.MissingManagePermission);

        var page = await _reader.SearchAsync(
            new IamAuditSearchQuery(
                query.Page,
                query.FilterActorUserId,
                query.FilterTargetUserId,
                string.IsNullOrWhiteSpace(query.EventType) ? null : query.EventType.Trim(),
                query.Outcome,
                query.FromUtc,
                query.ToUtc),
            cancellationToken);

        var items = page.Items
            .Select(r => new IamAuditLogDto(
                r.Id,
                r.OccurredAtUtc,
                r.EventType,
                r.Outcome,
                r.ActorUserId,
                r.TargetUserId,
                r.TargetRoleId,
                r.TargetPermissionId,
                r.CorrelationId,
                r.Metadata))
            .ToArray();

        return Result.Success(new PagedResult<IamAuditLogDto>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
