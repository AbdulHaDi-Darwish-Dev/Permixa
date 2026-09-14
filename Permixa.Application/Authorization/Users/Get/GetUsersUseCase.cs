using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Users.Models;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Paging;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Users.Get;

public sealed class GetUsersUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IRoleHierarchyReader _hierarchyReader;
    private readonly IIdentityUserReader _users;
    private readonly IClock _clock;

    public GetUsersUseCase(
        IEffectivePermissionService effectivePermissions,
        IRoleHierarchyReader hierarchyReader,
        IIdentityUserReader users,
        IClock clock)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchyReader = hierarchyReader;
        _users = users;
        _clock = clock;
    }

    public async Task<Result<PagedResult<IamUserDto>>> ExecuteAsync(
        GetUsersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = query.Page;
        if (!page.IsValid)
            return Result.Failure<PagedResult<IamUserDto>>(AuthorizationErrors.InvalidPaging);

        var permission = await UserAdministrationGuard.EnsurePermissionAsync(
            _effectivePermissions, query.ActorUserId, IamPermissions.Users.Read, cancellationToken);
        if (permission is not null)
            return Result.Failure<PagedResult<IamUserDto>>(permission.Error!);

        var actorLevel = await _hierarchyReader.GetEffectiveUserLevelAsync(query.ActorUserId, cancellationToken);
        if (!RoleHierarchyRules.HasAuthority(actorLevel) || actorLevel is null)
        {
            return Result.Success(new PagedResult<IamUserDto>([], page.Page, page.PageSize, 0));
        }

        var result = await _users.SearchManageableUsersAsync(
            new IdentityUserSearchQuery(
                query.ActorUserId,
                actorLevel.Value,
                _clock.UtcNow,
                query.Search,
                query.IsDisabled,
                query.IsLocked,
                page.Page,
                page.PageSize),
            cancellationToken);

        var items = result.Items.Select(UserMapping.ToDto).ToList();
        return Result.Success(new PagedResult<IamUserDto>(items, result.Page, result.PageSize, result.TotalCount));
    }
}
