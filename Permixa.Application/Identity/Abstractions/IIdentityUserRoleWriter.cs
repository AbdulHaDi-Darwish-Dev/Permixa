namespace Permixa.Application.Identity.Abstractions;

public interface IIdentityUserRoleWriter
{
    /// <returns>True when a membership row was added.</returns>
    Task<bool> AddToRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default);

    /// <returns>True when a membership row was removed.</returns>
    Task<bool> RemoveFromRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default);
}
