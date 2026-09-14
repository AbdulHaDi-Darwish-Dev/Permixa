using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Hierarchy;

namespace Permixa.Application.Tests.Authorization;

public sealed class RolePlacementCalculatorTests
{
    [Fact]
    public void DuplicateTiers_AreASingleOccupiedLevel()
    {
        var occupied = RolePlacementCalculator.OccupiedLevels([10, 10, 20]);

        Assert.Equal(new HashSet<int> { 10, 20 }, occupied);
    }

    [Fact]
    public void SameLevel_AssignsReferenceLevel_WithoutShift()
    {
        var plan = RolePlacementCalculator.Plan(RolePlacement.SameLevel, 10, new HashSet<int> { 10, 20 });

        Assert.True(plan.IsSuccess);
        Assert.Equal(10, plan.Value.AssignedLevel);
        Assert.False(plan.Value.RequiresShift);
        Assert.Empty(plan.Value.ShiftedFromLevels);
    }

    [Fact]
    public void Below_FreeSlot_DoesNotShiftWeakerGap()
    {
        var plan = RolePlacementCalculator.Plan(RolePlacement.Below, 10, new HashSet<int> { 10, 20 });

        Assert.True(plan.IsSuccess);
        Assert.Equal(11, plan.Value.AssignedLevel);
        Assert.False(plan.Value.RequiresShift);
    }

    [Fact]
    public void Below_Collision_ShiftsOnlyContiguousOccupiedChain()
    {
        var plan = RolePlacementCalculator.Plan(
            RolePlacement.Below,
            10,
            new HashSet<int> { 10, 11, 12, 20 });

        Assert.True(plan.IsSuccess);
        Assert.Equal(11, plan.Value.AssignedLevel);
        Assert.Equal(new[] { 11, 12 }, plan.Value.ShiftedFromLevels);
        Assert.DoesNotContain(20, plan.Value.ShiftedFromLevels);
        Assert.DoesNotContain(10, plan.Value.ShiftedFromLevels);
    }

    [Fact]
    public void Above_FreeWeakerSlot_ShiftsOnlyReferenceTier()
    {
        var plan = RolePlacementCalculator.Plan(
            RolePlacement.Above,
            10,
            new HashSet<int> { 5, 10, 20 });

        Assert.True(plan.IsSuccess);
        Assert.Equal(10, plan.Value.AssignedLevel);
        Assert.Equal(new[] { 10 }, plan.Value.ShiftedFromLevels);
        Assert.DoesNotContain(5, plan.Value.ShiftedFromLevels);
        Assert.DoesNotContain(20, plan.Value.ShiftedFromLevels);
    }

    [Fact]
    public void Above_CollisionChain_MovesWholeSameLevelTierTogether()
    {
        var occupied = RolePlacementCalculator.OccupiedLevels([5, 10, 10, 11, 12, 20]);
        var plan = RolePlacementCalculator.Plan(RolePlacement.Above, 10, occupied);

        Assert.True(plan.IsSuccess);
        Assert.Equal(10, plan.Value.AssignedLevel);
        Assert.Equal(new[] { 10, 11, 12 }, plan.Value.ShiftedFromLevels);
        Assert.DoesNotContain(5, plan.Value.ShiftedFromLevels);
        Assert.DoesNotContain(20, plan.Value.ShiftedFromLevels);
    }

    [Fact]
    public void OccupiedLevels_ExcludeOneOccurrence_CanSplitASameLevelTier()
    {
        var remaining = RolePlacementCalculator.OccupiedLevels([10, 10], excludeOneOccurrenceOfLevel: 10);

        Assert.Equal(new HashSet<int> { 10 }, remaining);

        var vacated = RolePlacementCalculator.OccupiedLevels([10], excludeOneOccurrenceOfLevel: 10);
        Assert.Empty(vacated);
    }

    [Fact]
    public void ShiftChain_StopsAtFirstFreeInteger_AndNeverUsesMinusOne()
    {
        var chain = RolePlacementCalculator.BuildContiguousOccupiedChain(10, new HashSet<int> { 10, 11, 12, 20 });

        Assert.True(chain.IsSuccess);
        Assert.Equal(new[] { 10, 11, 12 }, chain.Value);
        Assert.All(chain.Value, level => Assert.True(level >= 10));
    }

    [Fact]
    public void Below_IntMaxValue_DoesNotWrap()
    {
        var plan = RolePlacementCalculator.Plan(
            RolePlacement.Below,
            int.MaxValue,
            new HashSet<int> { int.MaxValue });

        Assert.True(plan.IsFailure);
        Assert.Equal(AuthorizationErrors.HierarchyLevelSpaceExhausted, plan.Error);
    }

    [Fact]
    public void OccupiedChain_AtIntMaxValue_DoesNotWrap()
    {
        var chain = RolePlacementCalculator.BuildContiguousOccupiedChain(
            int.MaxValue,
            new HashSet<int> { int.MaxValue });

        Assert.True(chain.IsFailure);
        Assert.Equal(AuthorizationErrors.HierarchyLevelSpaceExhausted, chain.Error);
    }

    [Fact]
    public void BelowFreeSlot_IsNeverAMidpointBetweenExistingTiers()
    {
        var plan = RolePlacementCalculator.Plan(RolePlacement.Below, 10, new HashSet<int> { 10, 20 });

        Assert.Equal(11, plan.Value.AssignedLevel);
        Assert.NotEqual(15, plan.Value.AssignedLevel);
    }
}
