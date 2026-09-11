using PowerFlow.App.Controller;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class EnvelopeActuationPolicyTests
{
    [Fact]
    public void ProductConfigDefault_IsAdaptiveWhileDisabledCompatibilityGateStillRejectsActuation()
    {
        Assert.True(PowerFlowConfig.Default.AdaptiveActuationEnabled);
        var result = Evaluate(enabled: false, auto: true, EnvelopeConfidence.High, manual: false, game: false, EnvelopeZone.Boost, EnvelopeZone.Boost);
        Assert.False(result.Eligible);
        Assert.Null(result.TargetState);
        Assert.Contains("advisory", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AutoMustBeEnabled()
    {
        var result = Evaluate(enabled: true, auto: false, EnvelopeConfidence.High, manual: false, game: false, EnvelopeZone.Boost, EnvelopeZone.Boost);
        Assert.False(result.Eligible);
        Assert.Null(result.TargetState);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ManualOrGameLatchAlwaysTakesPrecedence(bool manual, bool game)
    {
        var result = Evaluate(enabled: true, auto: true, EnvelopeConfidence.High, manual, game, EnvelopeZone.Boost, EnvelopeZone.Boost);
        Assert.False(result.Eligible);
        Assert.Null(result.TargetState);
        Assert.Contains("latch", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LowConfidenceCannotActuateAndMediumConfidenceCannotEscalateToBoost()
    {
        Assert.False(Evaluate(true, true, EnvelopeConfidence.Low, false, false, EnvelopeZone.Efficient, EnvelopeZone.Boost).Eligible);
        var mediumBoost = Evaluate(true, true, EnvelopeConfidence.Medium, false, false, EnvelopeZone.Boost, EnvelopeZone.Boost);
        Assert.False(mediumBoost.Eligible);
        Assert.Null(mediumBoost.TargetState);
    }

    [Fact]
    public void AppCeilingClampsGovernorOutputBeforePlanMapping()
    {
        var result = Evaluate(enabled: true, auto: true, EnvelopeConfidence.High, manual: false, game: false, EnvelopeZone.Boost, EnvelopeZone.Efficient);
        Assert.True(result.Eligible);
        Assert.Equal(EnvelopeZone.Efficient, result.EffectiveZone);
        Assert.Equal(PowerState.Balanced, result.TargetState);
        Assert.Contains("ceiling", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(EnvelopeZone.Eco, PowerState.PowerSaver)]
    [InlineData(EnvelopeZone.Efficient, PowerState.Balanced)]
    [InlineData(EnvelopeZone.Responsive, PowerState.Balanced)]
    [InlineData(EnvelopeZone.Boost, PowerState.Balanced)]
    public void SemanticZonesMapConservativelyToQualifiedWindowsStates(EnvelopeZone zone, PowerState expected)
    {
        var result = Evaluate(enabled: true, auto: true, EnvelopeConfidence.High, manual: false, game: false, zone, EnvelopeZone.Boost);
        Assert.True(result.Eligible);
        Assert.Equal(expected, result.TargetState);
    }

    [Theory]
    [InlineData(EnvelopeZone.Eco, PowerFlowOperatingMode.Saver, PowerState.PowerSaver)]
    [InlineData(EnvelopeZone.Efficient, PowerFlowOperatingMode.Balanced, PowerState.Balanced)]
    [InlineData(EnvelopeZone.Responsive, PowerFlowOperatingMode.BalancedPerformance, PowerState.Balanced)]
    [InlineData(EnvelopeZone.Boost, PowerFlowOperatingMode.Performance, PowerState.Balanced)]
    public void AutoZonesUseTheSameFixedPowerFlowProfilesAsManualModes(EnvelopeZone zone, PowerFlowOperatingMode mode, PowerState windowsState)
    {
        var profile = PowerFlowOperatingProfiles.ForAutoZone(zone);
        Assert.Equal(mode, profile.Mode);
        Assert.Equal(windowsState, profile.WindowsState);
    }

    private static EnvelopeActuationResult Evaluate(
        bool enabled,
        bool auto,
        EnvelopeConfidence confidence,
        bool manual,
        bool game,
        EnvelopeZone allowed,
        EnvelopeZone entitlementCeiling)
    {
        var governor = new GovernorDecision(allowed, allowed, EnvelopeDecisionKind.None, 0, null, "test", confidence);
        var entitlement = new PerformanceEntitlement(entitlementCeiling, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(6), true);
        return EnvelopeActuationPolicy.Evaluate(enabled, auto, confidence, manual, game, governor, entitlement);
    }
}
