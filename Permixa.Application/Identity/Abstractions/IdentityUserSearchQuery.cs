namespace Permixa.Application.Identity.Abstractions;

public sealed record IdentityUserSearchQuery(
    Guid ActorUserId,
    int ActorEffectiveLevel,
    DateTime UtcNow,
    string? Search,
    bool? IsDisabled,
    bool? IsLocked,
    int Page,
    int PageSize);
