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
    public void GameLatch_RemainsAutomaticAuthorityAndCannotMasqueradeAsManualUltra()
    {
        var model = TrayMenuCommands.Build(Snapshot(PowerState.HighPerformance, true, "Game"));
        Assert.Equal("Performance Locked - Game", model.StatusText);
        Assert.True(model.AutoChecked);
        Assert.False(model.UltraChecked);
        Assert.False(model.ReleaseLatchEnabled);
    }

    [Fact]
    public void ManualPerformanceProfile_IsDistinctFromUnderlyingBalancedPlan()
    {
        var model = TrayMenuCommands.Build(Snapshot(PowerState.Balanced, true, "Manual"), PowerFlowOperatingMode.Performance);
        Assert.Equal("Performance - Manual", model.StatusText);
        Assert.True(model.PerformanceChecked);
        Assert.False(model.BalancedChecked);
        Assert.False(model.AutoChecked);
        Assert.True(model.ReleaseLatchEnabled);
    }

    [Theory]
    [InlineData(PowerFlowOperatingMode.Saver, PowerState.PowerSaver, true, false, false, false)]
    [InlineData(PowerFlowOperatingMode.Balanced, PowerState.Balanced, false, true, false, false)]
    [InlineData(PowerFlowOperatingMode.Performance, PowerState.Balanced, false, false, true, false)]
    [InlineData(PowerFlowOperatingMode.Ultra, PowerState.HighPerformance, false, false, false, true)]
    public void ManualMode_ChecksExactPowerFlowProfile(PowerFlowOperatingMode mode, PowerState state, bool saver, bool balanced, bool performance, bool ultra)
    {
        var model = TrayMenuCommands.Build(Snapshot(state, true, "Manual"), mode);
        Assert.False(model.AutoChecked);
        Assert.Equal(saver, model.SaverChecked);
        Assert.Equal(balanced, model.BalancedChecked);
        Assert.Equal(performance, model.PerformanceChecked);
        Assert.Equal(ultra, model.UltraChecked);
    }

    [Fact]
    public void UnlatchedControllerState_ReportsAutoAuthorityRatherThanAPlanAsManualMode()
    {
        var model = TrayMenuCommands.Build(Snapshot(PowerState.Balanced));
        Assert.True(model.AutoChecked);
        Assert.False(model.SaverChecked);
        Assert.False(model.BalancedChecked);
        Assert.False(model.PerformanceChecked);
        Assert.False(model.UltraChecked);
    }

    [Fact]
    public void CommandIds_AreUnique()
    {
        var ids = new[] { TrayMenuCommands.OpenDashboard, TrayMenuCommands.Auto, TrayMenuCommands.PowerSaver, TrayMenuCommands.Balanced,
            TrayMenuCommands.HighPerformance, TrayMenuCommands.Ultra, TrayMenuCommands.ReleaseLatch, TrayMenuCommands.Settings, TrayMenuCommands.Exit };
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }
}