using PowerFlow.App.Controller;
using PowerFlow.Windows.Power;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class GraduatedProcessorPolicyPlannerTests
{
    [Fact]
    public void Plan_WhenCapacityTrailsRequest_RaisesCoreFloorGraduallyTowardFrequencyNormalizedRequirement()
    {
        var policy = new ProcessorPolicySnapshot(Guid.NewGuid(), 10, 60);
        var plan = GraduatedProcessorPolicyPlanner.Plan(38, 22, 51, policy);
        Assert.Equal(75, plan.EstimatedRequiredCoreFloorPercent, 3);
        Assert.Equal((uint)25, plan.NextCoreFloorPercent);
        Assert.True(plan.CoreFloorQualified);
        Assert.False(plan.EnergyPerformancePreferenceQualified);
        Assert.Equal((uint)60, plan.CurrentEnergyPerformancePreferencePercent);
    }

    [Fact]
    public void Plan_WhenDeliveredCapacityAlreadyCoversRequest_HoldsLowFloor()
    {
        var policy = new ProcessorPolicySnapshot(Guid.NewGuid(), 10, 60);
        var plan = GraduatedProcessorPolicyPlanner.Plan(20, 22.3, 51, policy);
        Assert.Equal((uint)10, plan.NextCoreFloorPercent);
        Assert.Contains("deadband", plan.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_WhenCapacityMateriallyExceedsRequest_ReleasesFloorMoreSlowly()
    {
        var policy = new ProcessorPolicySnapshot(Guid.NewGuid(), 75, 60);
        var plan = GraduatedProcessorPolicyPlanner.Plan(20, 38.2, 51, policy);
        Assert.Equal((uint)65, plan.NextCoreFloorPercent);
        Assert.Contains("release", plan.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_MissingFrequencyFailsSafeToNominalInsteadOfManufacturingPressure()
    {
        var policy = new ProcessorPolicySnapshot(Guid.NewGuid(), 10, 60);
        var plan = GraduatedProcessorPolicyPlanner.Plan(40, 20, null, policy);
        Assert.Equal(40, plan.EstimatedRequiredCoreFloorPercent, 3);
        Assert.Equal((uint)25, plan.NextCoreFloorPercent);
    }
}