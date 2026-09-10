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
    public void RecentEventLines_ProjectRealTransitionHistoryCompactly()
    {
        var vm = new DashboardViewModel();
        var at = new DateTimeOffset(2026, 9, 9, 14, 3, 0, TimeSpan.Zero);
        var history = new[]
        {
            new TransitionRecord(at.AddMinutes(-2), PowerState.Balanced, PowerState.PowerSaver, "System quiet", true),
            new TransitionRecord(at, PowerState.PowerSaver, PowerState.Balanced, "CPU demand sustained", true)
        };
        var snapshot = new ControllerSnapshot(PowerState.Balanced, "CPU demand sustained", false, null, 42, 0.6, null, null, at, history, 1, 1);

        vm.Update(snapshot, null);

        Assert.Equal(2, vm.RecentEventLines.Count);
        Assert.Contains("14:03", vm.RecentEventLines[0], StringComparison.Ordinal);
        Assert.Contains("Balanced", vm.RecentEventLines[0], StringComparison.Ordinal);
        Assert.Contains("CPU demand sustained", vm.RecentEventLines[0], StringComparison.Ordinal);
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

    [Fact]
    public void MemoryAndMachineTelemetry_ProjectsTruthfullyWhenAvailable()
    {
        var vm = new DashboardViewModel();
        var now = DateTimeOffset.UtcNow;
        var snapshot = new ControllerSnapshot(PowerState.Balanced, "test", false, null, 20, 0, null, null, now, [], 0, 0);

        vm.Update(snapshot, new DashboardTelemetry(42, 2200, now, 67.4, "reference-host"));

        Assert.Equal(67.4, vm.MemoryPercent);
        Assert.Equal("67%", vm.MemoryLabel);
        Assert.Equal("reference-host", vm.MachineLabel);
    }

    [Fact]
    public void MemoryAndMachineTelemetry_ShowsUnavailableInsteadOfInventingValues()
    {
        var vm = new DashboardViewModel();
        var now = DateTimeOffset.UtcNow;
        var snapshot = new ControllerSnapshot(PowerState.Balanced, "test", false, null, 20, 0, null, null, now, [], 0, 0);

        vm.Update(snapshot, new DashboardTelemetry(42, 2200, now));

        Assert.Null(vm.MemoryPercent);
        Assert.Equal("-", vm.MemoryLabel);
        Assert.Equal(string.Empty, vm.MachineLabel);
    }}
