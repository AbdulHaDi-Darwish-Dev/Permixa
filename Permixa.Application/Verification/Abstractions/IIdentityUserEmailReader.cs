namespace Permixa.Application.Verification.Abstractions;

/// <summary>
/// Identity-owned email lookup without exposing Identity types.
/// </summary>
public interface IIdentityUserEmailReader
{
    Task<Guid?> FindUserIdByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<string?> GetEmailAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> IsEmailConfirmedAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IdentityEmailProfile?> GetEmailProfileAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when another user already owns the normalized email or has it pending.
    /// </summary>
    Task<bool> IsEmailClaimedByAnotherUserAsync(
        Guid userId,
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when another user already owns the normalized Identity email.
    /// Pending addresses are ignored so two concurrent pending changes cannot deadlock confirmation.
    /// </summary>
    Task<bool> IsNormalizedEmailTakenByAnotherUserAsync(
        Guid userId,
        string email,
        CancellationToken cancellationToken = default);
}
