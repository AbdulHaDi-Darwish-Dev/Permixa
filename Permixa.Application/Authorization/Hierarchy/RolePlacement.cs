namespace Permixa.Application.Authorization.Hierarchy;

/// <summary>
/// Relative hierarchy placement against a reference role.
/// Permixa owns numeric RoleLevel allocation.
/// </summary>
public enum RolePlacement
{
    Above = 1,
    Below = 2,
    SameLevel = 3
}
