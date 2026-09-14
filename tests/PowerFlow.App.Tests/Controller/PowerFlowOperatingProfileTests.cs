using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class PowerFlowOperatingProfileTests
{
    [Fact]
    public void BalancedEfficientAndBalancedPerformance_AreDistinctReadinessStepsOnBalancedPlan()
    {
        var efficient = PowerFlowOperatingProfiles.Balanced;
        var performance = PowerFlowOperatingProfiles.BalancedPerformance;

        Assert.Equal(PowerState.Balanced, efficient.WindowsState);
        Assert.Equal(PowerState.Balanced, performance.WindowsState);
        Assert.Equal((uint)25, efficient.CoreFloorPercent);
        Assert.Equal((uint)50, performance.CoreFloorPercent);
        Assert.Equal((uint)35, efficient.EnergyPerformancePreferencePercent);
        Assert.Equal((uint)20, performance.EnergyPerformancePreferencePercent);
        Assert.Equal((uint)3, efficient.BoostMode);
        Assert.Equal((uint)3, performance.BoostMode);
        Assert.True(efficient.ReadinessFloorPercent < performance.ReadinessFloorPercent);
        Assert.True(performance.ReadinessFloorPercent < PowerFlowOperatingProfiles.Performance.ReadinessFloorPercent);
        Assert.Equal(60, efficient.MinimumResidencySeconds);
        Assert.Equal(60, performance.MinimumResidencySeconds);
        Assert.True(performance.PromotionQualificationSeconds < efficient.PromotionQualificationSeconds);
    }
}