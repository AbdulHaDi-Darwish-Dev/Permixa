using Permixa.Application.Common.Paging;

namespace Permixa.Application.Identity.Abstractions;

/// <summary>
/// Read-only Identity user capabilities required by Application Use Cases.
/// Implemented via ASP.NET Core Identity / EF without exposing UserManager.
/// </summary>
public interface IIdentityUserReader
{
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> GetUserRoleIdsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IdentityAccountState?> GetAccountStateAsync(
        Guid userId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<IdentityMfaLoginGate?> GetMfaLoginGateAsync(
        Guid userId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<IdentityUserIamRecord?> GetIamUserByIdAsync(
        Guid userId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<IdentityUserIamRecord?> GetIamUserByEmailAsync(
        string email,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<bool> HasRoleNameAsync(
        Guid userId,
        string roleName,
        CancellationToken cancellationToken = default);

    Task<PagedResult<IdentityUserIamRecord>> SearchManageableUsersAsync(
        IdentityUserSearchQuery query,
        CancellationToken cancellationToken = default);
}
