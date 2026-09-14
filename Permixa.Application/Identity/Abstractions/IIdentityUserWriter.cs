namespace Permixa.Application.Identity.Abstractions;

public interface IIdentityUserWriter
{
    Task<IdentityLockMutation> SetLockoutAsync(
        Guid userId,
        DateTime lockedUntilUtc,
        CancellationToken cancellationToken = default);

    Task<IdentityLockMutation> UnlockAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <returns>True when IsDisabled changed.</returns>
    Task<bool> SetDisabledAsync(
        Guid userId,
        bool isDisabled,
        CancellationToken cancellationToken = default);

    Task SetPendingEmailAsync(
        Guid userId,
        string? pendingEmail,
        CancellationToken cancellationToken = default);
}

public enum IdentityLockMutation
{
    NotFound,
    Unchanged,
    Updated
}
