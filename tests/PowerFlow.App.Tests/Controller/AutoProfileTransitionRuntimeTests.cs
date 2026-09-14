using PowerFlow.App.Controller;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class AutoProfileTransitionRuntimeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 13, 18, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan QuietWindow = TimeSpan.FromSeconds(25);

    [Fact]
    public void WindowsBalancedGate_PreventsSaverProfileFromOutrunningControllerHold()
    {
        var sut = new AutoProfileTransitionRuntime();
        var decision = sut.Evaluate(EnvelopeZone.Eco, PowerState.Balanced, null, T0, QuietWindow);
        Assert.True(decision.ShouldApply);
        Assert.Equal(PowerFlowOperatingMode.Balanced, decision.Profile.Mode);
    }

    [Fact]
    public void Promotion_UsesExistingProfileQualificationBeforeApplying()
    {
        var sut = new AutoProfileTransitionRuntime();
        sut.Commit(PowerFlowOperatingProfiles.Balanced, T0);
        var first = sut.Evaluate(EnvelopeZone.Responsive, PowerState.Balanced, PowerFlowOperatingProfiles.Balanced, T0.AddSeconds(1), QuietWindow);
        var qualified = sut.Evaluate(EnvelopeZone.Responsive, PowerState.Balanced, PowerFlowOperatingProfiles.Balanced, T0.AddSeconds(1.5), QuietWindow);
        Assert.False(first.ShouldApply);
        Assert.True(qualified.ShouldApply);
        Assert.Equal(PowerFlowOperatingMode.BalancedPerformance, qualified.Profile.Mode);
    }

    [Fact]
    public void Downshift_RequiresBothResidencyAndContinuousQuietQualification()
    {
        var sut = new AutoProfileTransitionRuntime();
        sut.Commit(PowerFlowOperatingProfiles.BalancedPerformance, T0);
        var firstDip = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(40), QuietWindow);
        var residencyReached = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(60), QuietWindow);
        var released = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(65), QuietWindow);
        Assert.False(firstDip.ShouldApply);
        Assert.False(residencyReached.ShouldApply);
        Assert.True(released.ShouldApply);
        Assert.Equal(PowerFlowOperatingMode.Balanced, released.Profile.Mode);
    }

    [Fact]
    public void Rebound_CancelsPendingDownshiftQualification()
    {
        var sut = new AutoProfileTransitionRuntime();
        sut.Commit(PowerFlowOperatingProfiles.BalancedPerformance, T0);
        sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(60), QuietWindow);
        var rebound = sut.Evaluate(EnvelopeZone.Responsive, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(70), QuietWindow);
        var secondDip = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(80), QuietWindow);
        var beforeRelease = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(104), QuietWindow);
        var released = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(105), QuietWindow);
        Assert.False(rebound.ShouldApply);
        Assert.False(secondDip.ShouldApply);
        Assert.False(beforeRelease.ShouldApply);
        Assert.True(released.ShouldApply);
    }

    [Fact]
    public void StrongRapidBounce_JumpsToMaximumBackoffWithoutDelayingCurrentPromotion()
    {
        var sut = new AutoProfileTransitionRuntime();
        sut.Commit(PowerFlowOperatingProfiles.BalancedPerformance, T0);
        sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(60), QuietWindow);
        var down = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(85), QuietWindow);
        sut.Commit(down.Profile, T0.AddSeconds(85));
        sut.Evaluate(EnvelopeZone.Responsive, PowerState.Balanced, PowerFlowOperatingProfiles.Balanced, T0.AddSeconds(86), QuietWindow);
        var promoted = sut.Evaluate(EnvelopeZone.Responsive, PowerState.Balanced, PowerFlowOperatingProfiles.Balanced, T0.AddSeconds(86.5), QuietWindow);
        sut.Commit(promoted.Profile, T0.AddSeconds(86.5));
        sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(366.5), QuietWindow);
        var stillHeldAtFourHundredSeconds = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(486.5), QuietWindow);
        var allowedAtMaximumBackoffBoundary = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.BalancedPerformance, T0.AddSeconds(566.5), QuietWindow);
        Assert.True(promoted.ShouldApply);
        Assert.False(stillHeldAtFourHundredSeconds.ShouldApply);
        Assert.Contains("/480s", stillHeldAtFourHundredSeconds.Reason, StringComparison.Ordinal);
        Assert.Contains("/200s", stillHeldAtFourHundredSeconds.Reason, StringComparison.Ordinal);
        Assert.True(allowedAtMaximumBackoffBoundary.ShouldApply);
    }

    [Fact]
    public void DeepDownshift_DecaysOnlyOneProfileStepAfterQualification()
    {
        var sut = new AutoProfileTransitionRuntime();
        sut.Commit(PowerFlowOperatingProfiles.Performance, T0);
        sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.Performance, T0.AddSeconds(30), QuietWindow);
        var decision = sut.Evaluate(EnvelopeZone.Efficient, PowerState.Balanced, PowerFlowOperatingProfiles.Performance, T0.AddSeconds(55), QuietWindow);
        Assert.True(decision.ShouldApply);
        Assert.Equal(PowerFlowOperatingMode.BalancedPerformance, decision.Profile.Mode);
    }

    [Fact]
    public void WindowsPlanTransition_OverridesProfileResidencyAndQuietQualification()
    {
        var sut = new AutoProfileTransitionRuntime();
        sut.Commit(PowerFlowOperatingProfiles.Balanced, T0);
        var decision = sut.Evaluate(EnvelopeZone.Eco, PowerState.PowerSaver, PowerFlowOperatingProfiles.Balanced, T0.AddSeconds(2), QuietWindow);
        Assert.True(decision.ShouldApply);
        Assert.Equal(PowerFlowOperatingMode.Saver, decision.Profile.Mode);
    }
}