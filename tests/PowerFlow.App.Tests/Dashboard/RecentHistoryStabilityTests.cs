using Xunit;
using System.ComponentModel;
using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.Core.Policy;
using PowerFlow.Windows.Activity;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class RecentHistoryStabilityTests
{
    [Fact]
    public void TelemetryOnlyRefresh_DoesNotReannounceUnchangedHistory()
    {
        var vm = new DashboardViewModel();
        var at = new DateTimeOffset(2026, 9, 8, 18, 0, 0, TimeSpan.Zero);
        var history = new[] { new TransitionRecord(at, PowerState.PowerSaver, PowerState.Balanced, "CPU demand sustained", true) };
        var snapshot1 = new ControllerSnapshot(PowerState.Balanced, "CPU demand sustained", false, null, 30, 0, null, null, at, history, 1, 0);
        vm.Update(snapshot1, new DashboardTelemetry(30, 3000, at));

        var historyNotifications = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DashboardViewModel.History)) historyNotifications++; };
        var snapshot2 = snapshot1 with { At = at.AddSeconds(1), History = history.ToArray(), CpuPercent = 29 };
        vm.Update(snapshot2, new DashboardTelemetry(29, 3000, at.AddSeconds(1)));

        Assert.Equal(0, historyNotifications);
        Assert.Single(vm.History);
    }

    [Fact]
    public void History_IsProjectedNewestFirst()
    {
        var vm = new DashboardViewModel();
        var t0 = DateTimeOffset.UtcNow;
        var history = new[]
        {
            new TransitionRecord(t0, PowerState.PowerSaver, PowerState.Balanced, "first", true),
            new TransitionRecord(t0.AddSeconds(10), PowerState.Balanced, PowerState.PowerSaver, "second", true)
        };
        vm.Update(new ControllerSnapshot(PowerState.PowerSaver, "second", false, null, 2, 0, null, null, t0.AddSeconds(10), history, 2, 0), null);

        Assert.Equal("second", vm.History[0].Reason);
    }
}
