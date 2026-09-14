using PowerFlow.App.Controller;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class AutoProfileTransitionRuntimeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 13, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WindowsBalancedGate_PreventsSaverProfileFromOutrunningControllerHold()
    {
        var sut = new AutoProfileTransitionRuntime();
        var decision = sut.Evaluate(EnvelopeZone.Eco, PowerState.Balanced, null, T0);
        Assert.True(decision.ShouldApply);
        Assert.Equal(PowerFlowOperatingMode.Balanced, decision.Profile.Mode);
    }

    [Fact]
    public void Promotion_UsesExistingProfileQualificationBeforeApplying()
    {
        var sut = new AutoProfileTransitionRuntime();
        sut.Commit(PowerFlowOperatingProfiles.Balanced, T0);
        var first = sut.Evaluate(EnvelopeZone.Responsive, PowerState.Balanced, PowerFlowOperatingProfiles.Balanced, T0.AddSeconds(1));
        var qualified = sut.Evaluate(EnvelopeZone.Responsive, PowerState.Balanced, PowerFlowOperatingProfiles.Balanced, T0.AddSeconds(1.5));
        Assert.False(first.ShouldApply);
        Assert.True(qualified.ShouldApply);
        Assert.Equal(PowerFlowOperatingMode.BalancedPerformance, qualified.Profile.Mode);
    }

    [Fact]
    public void BalancedPerformance_DoesNotDownshiftBeforeSixtySecondResidency()
    {
        var sut = new AutoProfileTransitionRuntime();
        sut.Commit(PowerFlowOperatingProfiles.BalancedPerformance, T0);
        var early = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(59));
        var released = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(60));
        Assert.False(early.ShouldApply);
        Assert.True(released.ShouldApply);
        Assert.Equal(PowerFlowOperatingMode.Balanced, released.Profile.Mode);
    }

    [Fact]
    public void RapidReentry_IsHeldForTenSecondsAfterDownshift()
    {
        var sut = new AutoProfileTransitionRuntime();
        sut.Commit(PowerFlowOperatingProfiles.BalancedPerformance, T0);
        var down = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(60));
        Assert.True(down.ShouldApply);
        sut.Commit(down.Profile, T0.AddSeconds(60));

        var rebound = sut.Evaluate(EnvelopeZone.Responsive, PowerState.Balanced, PowerFlowOperatingProfiles.Balanced, T0.AddSeconds(65));
        var qualifying = sut.Evaluate(EnvelopeZone.Responsive, PowerState.Balanced, PowerFlowOperatingProfiles.Balanced, T0.AddSeconds(70));
        var promoted = sut.Evaluate(EnvelopeZone.Responsive, PowerState.Balanced, PowerFlowOperatingProfiles.Balanced, T0.AddSeconds(71));

        Assert.False(rebound.ShouldApply);
        Assert.False(qualifying.ShouldApply);
        Assert.True(promoted.ShouldApply);
    }

    [Fact]
    public void RapidBounce_DoublesNextHighProfileResidency()
    {
        var sut = new AutoProfileTransitionRuntime();
        sut.Commit(PowerFlowOperatingProfiles.BalancedPerformance, T0);
        var down = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(60));
        sut.Commit(down.Profile, T0.AddSeconds(60));
        sut.Evaluate(EnvelopeZone.Responsive, PowerState.Balanced, PowerFlowOperatingProfiles.Balanced, T0.AddSeconds(70));
        var promoted = sut.Evaluate(EnvelopeZone.Responsive, PowerState.Balanced, PowerFlowOperatingProfiles.Balanced, T0.AddSeconds(71));
        sut.Commit(promoted.Profile, T0.AddSeconds(71));

        var tooSoon = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(190));
        var allowed = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(191));

        Assert.False(tooSoon.ShouldApply);
        Assert.True(allowed.ShouldApply);
        Assert.Equal(PowerFlowOperatingMode.Balanced, allowed.Profile.Mode);
    }

    [Fact]
    public void DeepDownshift_DecaysOnlyOneProfileStepAfterResidency()
    {
        var sut = new AutoProfileTransitionRuntime();
        sut.Commit(PowerFlowOperatingProfiles.Performance, T0);
        var decision = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.Performance, T0.AddSeconds(30));
        Assert.True(decision.ShouldApply);
        Assert.Equal(PowerFlowOperatingMode.BalancedPerformance, decision.Profile.Mode);
    }

    [Fact]
    public void WindowsPlanTransition_OverridesProfileResidencyToKeepSchemesAligned()
    {
        var sut = new AutoProfileTransitionRuntime();
        sut.Commit(PowerFlowOperatingProfiles.Balanced, T0);
        var decision = sut.Evaluate(EnvelopeZone.Eco, PowerState.PowerSaver, PowerFlowOperatingProfiles.Balanced, T0.AddSeconds(2));
        Assert.True(decision.ShouldApply);
        Assert.Equal(PowerFlowOperatingMode.Saver, decision.Profile.Mode);
    }
}