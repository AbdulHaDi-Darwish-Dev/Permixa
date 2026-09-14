using Permixa.Application.Common.Paging;

namespace Permixa.Application.Authorization.Users.Get;

public sealed record GetUsersQuery(
    Guid ActorUserId,
    PageRequest Page,
    string? Search = null,
    bool? IsDisabled = null,
    bool? IsLocked = null);
