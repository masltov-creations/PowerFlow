using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class PowerFlowOperatingProfileTests
{
    [Fact]
    public void Profiles_FormIncreasingReadinessLadder()
    {
        var saver = PowerFlowOperatingProfiles.Saver;
        var balanced = PowerFlowOperatingProfiles.Balanced;
        var performance = PowerFlowOperatingProfiles.Performance;
        var ultra = PowerFlowOperatingProfiles.Ultra;
        Assert.Equal(PowerState.PowerSaver, saver.WindowsState);
        Assert.Equal((uint)0, saver.BoostMode);
        Assert.True(saver.CoreFloorPercent < balanced.CoreFloorPercent);
        Assert.True(balanced.CoreFloorPercent < performance.CoreFloorPercent);
        Assert.True(performance.CoreFloorPercent < ultra.CoreFloorPercent);
        Assert.Equal((uint)100, ultra.CoreFloorPercent);
        Assert.Equal(PowerState.HighPerformance, ultra.WindowsState);
        Assert.True(saver.PromotionQualificationSeconds > balanced.PromotionQualificationSeconds);
        Assert.True(balanced.PromotionQualificationSeconds > performance.PromotionQualificationSeconds);
    }
}