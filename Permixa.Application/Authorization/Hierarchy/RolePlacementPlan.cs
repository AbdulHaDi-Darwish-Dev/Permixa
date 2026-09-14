namespace Permixa.Application.Authorization.Hierarchy;

/// <summary>
/// Result of collision-only placement.
/// <see cref="ShiftedFromLevels"/> are existing occupied tiers that move +1 (weakest first when applied).
/// </summary>
public sealed record RolePlacementPlan(
    int AssignedLevel,
    IReadOnlyList<int> ShiftedFromLevels)
{
    public bool RequiresShift => ShiftedFromLevels.Count > 0;
}
