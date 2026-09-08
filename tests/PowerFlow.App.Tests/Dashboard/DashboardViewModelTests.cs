using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.Core.Policy;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class DashboardViewModelTests
{
    [Fact]
    public void SnapshotProjectsWhereWhyAndWhatNext()
    {
        var vm = new DashboardViewModel();
        var history = new[] { new TransitionRecord(DateTimeOffset.UtcNow, PowerState.PowerSaver, PowerState.Balanced, "CPU demand sustained", true) };
        var snapshot = new ControllerSnapshot(PowerState.Balanced, "CPU demand sustained", false, null, 62.5, 0.74, null, "render.exe", DateTimeOffset.UtcNow, history, 8, 0);

        vm.Update(snapshot, new DashboardTelemetry(58.4, 4210, DateTimeOffset.UtcNow));

        Assert.Equal("BALANCED", vm.StateLabel);
        Assert.Equal("CPU demand sustained", vm.Reason);
        Assert.Equal("render.exe", vm.TriggerApplication);
        Assert.Equal("Returns to Power Saver when quiet", vm.NextActionLabel);
        Assert.Equal("62.5%", vm.CpuLabel);
        Assert.Equal("58.4 W", vm.WattsLabel);
        Assert.Equal("4.21 GHz", vm.FrequencyLabel);
        Assert.Equal(0.74, vm.FlowProgress, 3);
        Assert.Single(vm.History);
    }

    [Fact]
    public void GameLatchProjectsNonDemotingLockedState()
    {
        var vm = new DashboardViewModel();
        var snapshot = new ControllerSnapshot(PowerState.HighPerformance, "Game detected", true, "Game", 4.0, 1, null, "game.exe", DateTimeOffset.UtcNow, [], 20, 0);

        vm.Update(snapshot, null);

        Assert.Equal("PERFORMANCE LOCKED", vm.StateLabel);
        Assert.Equal("Game", vm.BadgeLabel);
        Assert.Equal("Held until game exits", vm.NextActionLabel);
        Assert.Equal("—", vm.WattsLabel);
    }
}

