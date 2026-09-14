namespace Permixa.Application.Identity.Abstractions;

/// <summary>
/// Read-only Identity role capabilities required by Application Use Cases.
/// Implemented via ASP.NET Core Identity without exposing RoleManager.
/// </summary>
public interface IIdentityRoleReader
{
    Task<bool> RoleExistsAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<IdentityRoleRecord?> GetByIdAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<IdentityRoleRecord?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IdentityRoleRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> HasAssignedUsersAsync(Guid roleId, CancellationToken cancellationToken = default);
}
