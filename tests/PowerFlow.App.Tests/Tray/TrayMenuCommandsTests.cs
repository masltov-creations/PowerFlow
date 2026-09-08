using PowerFlow.App.Controller;
using PowerFlow.App.Tray;
using PowerFlow.Core.Policy;
using Xunit;

namespace PowerFlow.App.Tests.Tray;

public sealed class TrayMenuCommandsTests
{
    private static ControllerSnapshot Snapshot(PowerState state, bool latched = false, string? latchType = null) =>
        new(state, latched ? $"Performance locked - {latchType}" : state.ToString(), latched, latchType, 4, 0, null, null,
            DateTimeOffset.UnixEpoch, Array.Empty<TransitionRecord>(), 0, 0);

    [Fact]
    public void GameLatch_ShowsLockedStatusAndReleaseCommand()
    {
        var model = TrayMenuCommands.Build(Snapshot(PowerState.HighPerformance, true, "Game"));
        Assert.Equal("Performance Locked · Game", model.StatusText);
        Assert.False(model.ReleaseLatchEnabled);
        Assert.True(model.HighPerformanceChecked);
    }

    [Fact]
    public void ManualLatch_ShowsManualLock()
    {
        var model = TrayMenuCommands.Build(Snapshot(PowerState.HighPerformance, true, "Manual"));
        Assert.Equal("Performance Locked · Manual", model.StatusText);
        Assert.True(model.ReleaseLatchEnabled);
    }

    [Theory]
    [InlineData(PowerState.PowerSaver, true, false, false)]
    [InlineData(PowerState.Balanced, false, true, false)]
    [InlineData(PowerState.HighPerformance, false, false, true)]
    public void CurrentState_IsChecked(PowerState state, bool saver, bool balanced, bool performance)
    {
        var model = TrayMenuCommands.Build(Snapshot(state));
        Assert.Equal(saver, model.PowerSaverChecked);
        Assert.Equal(balanced, model.BalancedChecked);
        Assert.Equal(performance, model.HighPerformanceChecked);
        Assert.False(model.ReleaseLatchEnabled);
    }

    [Fact]
    public void CommandIds_AreUnique()
    {
        var ids = new[] { TrayMenuCommands.OpenDashboard, TrayMenuCommands.PowerSaver, TrayMenuCommands.Balanced,
            TrayMenuCommands.HighPerformance, TrayMenuCommands.ReleaseLatch, TrayMenuCommands.Settings, TrayMenuCommands.Exit };
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }
}

