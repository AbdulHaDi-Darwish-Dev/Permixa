namespace Permixa.Application.Authorization.Abstractions;

/// <summary>
/// Reads role hierarchy levels from Identity roles without exposing Identity types.
/// Smaller RoleLevel = higher authority.
/// </summary>
public interface IRoleHierarchyReader
{
    /// <summary>
    /// Effective user level = minimum RoleLevel among assigned roles.
    /// Returns null when the user has no roles (no administrative authority).
    /// </summary>
    Task<int?> GetEffectiveUserLevelAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the RoleLevel for a role, or null when the role does not exist.
    /// </summary>
    Task<int?> GetRoleLevelAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);
}
