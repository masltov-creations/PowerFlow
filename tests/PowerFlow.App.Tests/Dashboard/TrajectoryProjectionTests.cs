using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class TrajectoryProjectionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateBuildsStateSegmentsAndHonestGap()
    {
        var samples = new[]
        {
            Sample(0, 8, PowerState.PowerSaver),
            Sample(5, 10, PowerState.PowerSaver),
            Sample(10, 50, PowerState.Balanced),
            Sample(40, 20, PowerState.Balanced),
            Sample(45, 8, PowerState.PowerSaver)
        };
        var model = TrajectoryProjection.Create(samples, Snapshot(PowerState.PowerSaver, 8), Config());

        Assert.Equal(4, model.StateSegments.Count);
        Assert.Equal(PowerState.Balanced, model.StateSegments[1].State);
        Assert.Equal(PowerState.Balanced, model.StateSegments[2].State);
        Assert.Single(model.Gaps);
        Assert.Equal(T0.AddSeconds(10), model.Gaps[0].From);
        Assert.Equal(T0.AddSeconds(40), model.Gaps[0].To);
    }

    [Fact]
    public void CreateOnlyMarksRealSemanticTransitionsOrFailures()
    {
        var history = new[]
        {
            new TransitionRecord(T0.AddSeconds(5), PowerState.PowerSaver, PowerState.PowerSaver, "same", true),
            new TransitionRecord(T0.AddSeconds(10), PowerState.PowerSaver, PowerState.Balanced, "load", true),
            new TransitionRecord(T0.AddSeconds(15), PowerState.Balanced, PowerState.HighPerformance, "failed", false)
        };
        var snapshot = Snapshot(PowerState.Balanced, 40) with { History = history };
        var model = TrajectoryProjection.Create(Array.Empty<ContinuitySample>(), snapshot, Config());

        Assert.Equal(2, model.Transitions.Count);
        Assert.DoesNotContain(model.Transitions, x => x.Reason == "same");
        Assert.Contains(model.Transitions, x => x.Reason == "load" && x.Success);
        Assert.Contains(model.Transitions, x => x.Reason == "failed" && !x.Success);
    }

    [Fact]
    public void CreateExposesNowAndPolicyRailsWithoutPredictingFutureCpu()
    {
        var config = Config() with
        {
            QuietThresholdPercent = 21,
            QuietWindow = TimeSpan.FromSeconds(25),
            CpuPromotionThresholdPercent = 44,
            CpuPromotionWindow = TimeSpan.FromSeconds(4)
        };
        var model = TrajectoryProjection.Create(Array.Empty<ContinuitySample>(), Snapshot(PowerState.PowerSaver, 46), config);

        Assert.Equal(PowerState.PowerSaver, model.Now.State);
        Assert.Equal(46, model.Now.CpuPercent);
        Assert.Equal(21, model.QuietRail.ThresholdPercent);
        Assert.Equal(TimeSpan.FromSeconds(25), model.QuietRail.HoldTime);
        Assert.Equal(44, model.PromoteRail.ThresholdPercent);
        Assert.Equal(TimeSpan.FromSeconds(4), model.PromoteRail.HoldTime);
        Assert.Equal(TrajectoryDirection.TowardBalanced, model.Direction);
        Assert.Contains("Balanced", model.NextAction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("future CPU", model.NextAction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("will be", model.NextAction, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Game", TrajectoryDirection.HoldPerformance)]
    [InlineData("Manual", TrajectoryDirection.HoldManual)]
    public void LatchesProjectHoldDirection(string latchType, TrajectoryDirection expected)
    {
        var model = TrajectoryProjection.Create(Array.Empty<ContinuitySample>(), Snapshot(PowerState.HighPerformance, 2, true, latchType), Config());
        Assert.Equal(expected, model.Direction);
        Assert.Contains("held", model.NextAction, StringComparison.OrdinalIgnoreCase);
    }

    private static ContinuitySample Sample(int seconds, double cpu, PowerState state) =>
        new(T0.AddSeconds(seconds), cpu, null, null, state, "test", false, null, 0, null);

    private static ControllerSnapshot Snapshot(PowerState state, double cpu, bool latched = false, string? latchType = null) =>
        new(state, "test", latched, latchType, cpu, .5, null, null, T0.AddSeconds(50), Array.Empty<TransitionRecord>(), 0, 0);

    private static PowerFlowConfig Config() => PowerFlowConfig.Default;
}
