namespace Permixa.Application.Authorization.Hierarchy;

/// <summary>
/// Pure hierarchy comparison rules.
/// Smaller RoleLevel number = higher authority.
/// Same-level management is not allowed by default.
/// </summary>
public static class RoleHierarchyRules
{
    /// <summary>
    /// Returns true when the actor may manage the target under default policy:
    /// ActorLevel &lt; TargetLevel.
    /// </summary>
    public static bool CanManage(int actorLevel, int targetLevel) =>
        actorLevel < targetLevel;

    /// <summary>
    /// Returns true when the actor may assign or create a role at <paramref name="targetRoleLevel"/>.
    /// Actor must be strictly higher authority than the target role.
    /// </summary>
    public static bool CanControlRoleLevel(int actorLevel, int targetRoleLevel) =>
        actorLevel < targetRoleLevel;

    /// <summary>
    /// Effective authority is the minimum RoleLevel among assigned roles.
    /// Empty assignment yields null (no authority).
    /// </summary>
    public static int? ComputeEffectiveLevel(IEnumerable<int> roleLevels)
    {
        ArgumentNullException.ThrowIfNull(roleLevels);

        int? effective = null;

        foreach (var level in roleLevels)
        {
            if (effective is null || level < effective.Value)
                effective = level;
        }

        return effective;
    }

    /// <summary>
    /// Users without a role have no hierarchical authority.
    /// </summary>
    public static bool HasAuthority(int? effectiveLevel) => effectiveLevel.HasValue;
}
