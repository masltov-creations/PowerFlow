using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using Xunit;

namespace PowerFlow.Core.Tests.Rules;

public sealed class ConfigTests
{
    [Fact]
    public void Defaults_AreConservativeAndMatchDesign()
    {
        var c = PowerFlowConfig.Default;
        Assert.Equal(1, c.SchemaVersion);
        Assert.Equal(PowerState.PowerSaver, c.RestingState);
        Assert.Equal(35d, c.CpuPromotionThresholdPercent);
        Assert.Equal(TimeSpan.FromSeconds(4), c.CpuPromotionWindow);
        Assert.Equal(12d, c.QuietThresholdPercent);
        Assert.Equal(TimeSpan.FromSeconds(25), c.QuietWindow);
        Assert.Equal(TimeSpan.FromSeconds(8), c.PostGameCooldown);
        Assert.Empty(c.AppRules);
        Assert.Null(c.PowerSaverPlanId);
        Assert.Null(c.BalancedPlanId);
        Assert.Null(c.HighPerformancePlanId);
    }
}
