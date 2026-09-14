using Permixa.Application.Common.Results;

namespace Permixa.Application.Authorization.Hierarchy;

/// <summary>
/// Pure collision-only RoleLevel placement. No midpoint and no spacing constant.
/// Shifts move only toward weaker authority (+1) and stop at the first free integer.
/// </summary>
public static class RolePlacementCalculator
{
    public static Result<RolePlacementPlan> Plan(
        RolePlacement placement,
        int referenceLevel,
        IReadOnlySet<int> occupiedLevels)
    {
        ArgumentNullException.ThrowIfNull(occupiedLevels);

        if (referenceLevel < 1)
            return Result.Failure<RolePlacementPlan>(AuthorizationErrors.InvalidRolePlacement);

        return placement switch
        {
            RolePlacement.SameLevel => Result.Success(new RolePlacementPlan(referenceLevel, [])),
            RolePlacement.Below => PlanBelow(referenceLevel, occupiedLevels),
            RolePlacement.Above => PlanAbove(referenceLevel, occupiedLevels),
            _ => Result.Failure<RolePlacementPlan>(AuthorizationErrors.InvalidRolePlacement)
        };
    }

    public static IReadOnlySet<int> OccupiedLevels(
        IEnumerable<int> roleLevels,
        int? excludeOneOccurrenceOfLevel = null)
    {
        ArgumentNullException.ThrowIfNull(roleLevels);

        var counts = new Dictionary<int, int>();
        foreach (var level in roleLevels)
        {
            counts.TryGetValue(level, out var count);
            counts[level] = count + 1;
        }

        if (excludeOneOccurrenceOfLevel is int excluded
            && counts.TryGetValue(excluded, out var excludedCount))
        {
            if (excludedCount <= 1)
                counts.Remove(excluded);
            else
                counts[excluded] = excludedCount - 1;
        }

        return counts.Keys.ToHashSet();
    }

    private static Result<RolePlacementPlan> PlanBelow(int referenceLevel, IReadOnlySet<int> occupiedLevels)
    {
        int target;
        try
        {
            target = checked(referenceLevel + 1);
        }
        catch (OverflowException)
        {
            return Result.Failure<RolePlacementPlan>(AuthorizationErrors.HierarchyLevelSpaceExhausted);
        }

        if (!occupiedLevels.Contains(target))
            return Result.Success(new RolePlacementPlan(target, []));

        var chain = BuildContiguousOccupiedChain(target, occupiedLevels);
        if (chain.IsFailure)
            return Result.Failure<RolePlacementPlan>(chain.Error!);

        return Result.Success(new RolePlacementPlan(target, chain.Value));
    }

    private static Result<RolePlacementPlan> PlanAbove(int referenceLevel, IReadOnlySet<int> occupiedLevels)
    {
        if (!occupiedLevels.Contains(referenceLevel))
            return Result.Success(new RolePlacementPlan(referenceLevel, []));

        var chain = BuildContiguousOccupiedChain(referenceLevel, occupiedLevels);
        if (chain.IsFailure)
            return Result.Failure<RolePlacementPlan>(chain.Error!);

        return Result.Success(new RolePlacementPlan(referenceLevel, chain.Value));
    }

    /// <summary>
    /// Occupied integers from <paramref name="startLevel"/> through the last occupied
    /// integer before the first free level. Empty if <paramref name="startLevel"/> is free.
    /// </summary>
    public static Result<IReadOnlyList<int>> BuildContiguousOccupiedChain(
        int startLevel,
        IReadOnlySet<int> occupiedLevels)
    {
        ArgumentNullException.ThrowIfNull(occupiedLevels);

        var chain = new List<int>();
        var level = startLevel;

        while (occupiedLevels.Contains(level))
        {
            chain.Add(level);
            if (level == int.MaxValue)
                return Result.Failure<IReadOnlyList<int>>(AuthorizationErrors.HierarchyLevelSpaceExhausted);

            try
            {
                level = checked(level + 1);
            }
            catch (OverflowException)
            {
                return Result.Failure<IReadOnlyList<int>>(AuthorizationErrors.HierarchyLevelSpaceExhausted);
            }
        }

        return Result.Success<IReadOnlyList<int>>(chain);
    }
}
