namespace Permixa.Application.Identity.Abstractions;

public interface IIdentityUserRoleReader
{
    Task<bool> IsInRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IdentityRoleRecord>> GetRolesForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IamUserRecord>> GetUsersInRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);
}
