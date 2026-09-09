using PowerFlow.App.Controller;
using PowerFlow.App.Telemetry;
using PowerFlow.App.Tray;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Tray;

public sealed class TrayMiniTrajectoryProjectionTests
{
    [Fact]
    public void CreateUsesPreExistingContinuityAndSharedTrajectorySemantics()
    {
        var t0 = new DateTimeOffset(2026, 9, 8, 23, 45, 0, TimeSpan.Zero);
        var history = new[]
        {
            new ContinuitySample(t0, 8, 48, 3200, PowerState.PowerSaver, "quiet", false, null, 0, null),
            new ContinuitySample(t0.AddSeconds(5), 46, 74, 4200, PowerState.Balanced, "load", false, null, .7, "build.exe")
        };
        var transition = new TransitionRecord(t0.AddSeconds(5), PowerState.PowerSaver, PowerState.Balanced, "load sustained", true);
        var snapshot = new ControllerSnapshot(PowerState.Balanced, "load sustained", false, null, 46, .7, null, "build.exe", t0.AddSeconds(5), new[] { transition }, 0, 0);

        var model = TrayMiniTrajectoryProjection.Create(history, snapshot, PowerFlowConfig.Default, new DashboardTelemetry(74, 4200, t0.AddSeconds(5)));

        Assert.Equal(2, model.Samples.Count);
        Assert.Equal("BALANCED", model.StateLabel);
        Assert.Equal("AUTO", model.ModeBadge);
        Assert.Equal("46.0%", model.CpuLabel);
        Assert.Equal("74.0 W", model.WattsLabel);
        Assert.Equal("4.20", model.FrequencyLabel);
        Assert.Single(model.Transitions);
        Assert.Equal(PowerState.Balanced, model.Trajectory.Now.State);
    }

    [Fact]
    public void MissingRichTelemetryStaysHonestAndGameLatchUsesSharedNextAction()
    {
        var at = DateTimeOffset.UtcNow;
        var snapshot = new ControllerSnapshot(PowerState.HighPerformance, "game", true, "Game", 3, 1, null, "game.exe", at, [], 0, 0);

        var model = TrayMiniTrajectoryProjection.Create(Array.Empty<ContinuitySample>(), snapshot, PowerFlowConfig.Default, null);

        Assert.Equal("PERFORMANCE", model.StateLabel);
        Assert.Equal("GAME LOCK", model.ModeBadge);
        Assert.Equal("—", model.WattsLabel);
        Assert.Equal("—", model.FrequencyLabel);
        Assert.Contains("game exits", model.NextAction, StringComparison.OrdinalIgnoreCase);
    }
}
