using PowerFlow.App.Controller;
using PowerFlow.App.Tray;
using PowerFlow.Core.Policy;
using Xunit;

namespace PowerFlow.App.Tests.Tray;

public sealed class TrayIconMotionPolicyTests
{
    private static ControllerSnapshot Snapshot(PowerState state, bool latched = false, string? latchType = null) =>
        new(state, state.ToString(), latched, latchType, 4, 0, null, null, DateTimeOffset.UnixEpoch, Array.Empty<TransitionRecord>(), 0, 0);

    [Fact]
    public void EquivalentSettledSnapshots_DoNotAnimate()
    {
        var plan = TrayIconMotionPolicy.Decide(Snapshot(PowerState.Balanced), Snapshot(PowerState.Balanced), null, null);
        Assert.Equal(TrayIconMotionKind.None, plan.Kind);
        Assert.Empty(plan.FrameIndices);
    }

    [Fact]
    public void Promotion_UsesFiniteForwardSequenceAndSettlesAtResponsivePosture()
    {
        var plan = TrayIconMotionPolicy.Decide(Snapshot(PowerState.PowerSaver), Snapshot(PowerState.HighPerformance), null, null);
        Assert.Equal(TrayIconMotionKind.Promote, plan.Kind);
        Assert.InRange(plan.Duration.TotalMilliseconds, 350, 700);
        Assert.InRange(plan.FrameInterval.TotalMilliseconds, 80, 125);
        Assert.InRange(plan.FrameIndices.Count, 4, 7);
        Assert.Equal(4, plan.FrameIndices[^1]);
        Assert.All(plan.FrameIndices, i => Assert.InRange(i, 0, 4));
    }

    [Fact]
    public void Demotion_UsesFiniteReverseSequenceAndSettlesRelaxed()
    {
        var plan = TrayIconMotionPolicy.Decide(Snapshot(PowerState.HighPerformance), Snapshot(PowerState.PowerSaver), null, null);
        Assert.Equal(TrayIconMotionKind.Settle, plan.Kind);
        Assert.Equal(0, plan.FrameIndices[^1]);
        Assert.True(plan.FrameIndices.Zip(plan.FrameIndices.Skip(1), (a, b) => b <= a + 1).All(v => v));
    }

    [Fact]
    public void ManualProfilesMapToDistinctSettledPosturesEvenOnSameWindowsState()
    {
        var balanced = Snapshot(PowerState.Balanced, true, "Manual");
        Assert.Equal(1, TrayIconMotionPolicy.SettledFrame(balanced, PowerFlowOperatingMode.Balanced));
        Assert.Equal(3, TrayIconMotionPolicy.SettledFrame(balanced, PowerFlowOperatingMode.BalancedPerformance));
        Assert.Equal(4, TrayIconMotionPolicy.SettledFrame(balanced, PowerFlowOperatingMode.Performance));
        var plan = TrayIconMotionPolicy.Decide(balanced, balanced, PowerFlowOperatingMode.Balanced, PowerFlowOperatingMode.Performance);
        Assert.Equal(TrayIconMotionKind.Promote, plan.Kind);
        Assert.Equal(4, plan.FrameIndices[^1]);
    }

    [Fact]
    public void AuthorityChangeAtSamePosture_GetsOneFiniteHoldGesture()
    {
        var previous = Snapshot(PowerState.Balanced, false);
        var current = Snapshot(PowerState.Balanced, true, "Game");
        var plan = TrayIconMotionPolicy.Decide(previous, current, null, null);
        Assert.Equal(TrayIconMotionKind.Hold, plan.Kind);
        Assert.NotEmpty(plan.FrameIndices);
        Assert.Equal(TrayIconMotionPolicy.SettledFrame(current, null), plan.FrameIndices[^1]);
    }
}