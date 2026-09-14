namespace Permixa.Application.Identity.Abstractions;

/// <summary>
/// Live Identity account state for authentication and refresh boundaries.
/// </summary>
public sealed record IdentityAccountState(
    Guid UserId,
    bool IsDisabled,
    bool IsLocked);
