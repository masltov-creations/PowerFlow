using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using Xunit;

namespace PowerFlow.Core.Tests.Rules;

public sealed class ConfigTests
{
    [Fact]
    public void LegacyRules_ProjectSemanticEntitlements()
    {
        var balanced = new AppRule("c:\\apps\\browser.exe", AppRuleMode.Balanced);
        var performance = new AppRule("c:\\apps\\game.exe", AppRuleMode.Performance);

        Assert.Equal(PowerFlow.Core.Envelope.EnvelopeZone.Efficient, balanced.EffectiveEntitlement.MaximumZone);
        Assert.Equal(PowerFlow.Core.Envelope.EnvelopeZone.Boost, performance.EffectiveEntitlement.MaximumZone);
        Assert.Null(balanced.Entitlement);
    }

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
        Assert.Equal(500, c.TelemetryVisibleIntervalMs);
        Assert.Equal(TimeSpan.FromMilliseconds(500), c.EffectiveTelemetryVisibleInterval);
        Assert.Equal(5000, c.TelemetryBackgroundIntervalMs);
        Assert.Empty(c.AppRules);
        Assert.Null(c.PowerSaverPlanId);
        Assert.Null(c.BalancedPlanId);
        Assert.Null(c.HighPerformancePlanId);
    }
    [Fact]
    public void GovernorTension_DefaultsToNeutralFifty()
    {
        Assert.Null(PowerFlowConfig.Default.GovernorTensionPercent);
        Assert.Equal(50d, PowerFlowConfig.Default.EffectiveGovernorTensionPercent);
    }

    [Theory]
    [InlineData(-20, 0)]
    [InlineData(0, 0)]
    [InlineData(37.5, 37.5)]
    [InlineData(100, 100)]
    [InlineData(140, 100)]
    public void GovernorTension_EffectiveValueClampsPersistedInput(double configured, double expected)
    {
        var config = PowerFlowConfig.Default with { GovernorTensionPercent = configured };
        Assert.Equal(expected, config.EffectiveGovernorTensionPercent);
    }
}
