namespace Permixa.Application.Identity.Abstractions;

/// <summary>
/// Access to the user-specific authorization version stored on ApplicationUser.
/// Does not define a User entity in Application.
/// </summary>
public interface IUserAuthorizationVersionStore
{
    Task<int> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task IncrementAsync(Guid userId, CancellationToken cancellationToken = default);
}
