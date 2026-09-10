using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class OperatingHistoryViewModelTests
{
    [Fact]
    public void Update_ProjectsExistingTelemetryIntoOperatingHistoryWithoutInventingUnavailableDimensions()
    {
        var vm = new DashboardViewModel();
        var at = new DateTimeOffset(2026, 9, 9, 18, 31, 0, TimeSpan.Zero);
        var snapshot = new ControllerSnapshot(PowerState.Balanced, "test", false, null, 42.5, 0.2, null, null, at, [], 1, 1);

        vm.Update(snapshot, new DashboardTelemetry(47.2, 2150, at));

        var observation = Assert.Single(vm.OperatingHistory);
        Assert.Equal(at, observation.At);
        Assert.Equal(42.5, observation.CpuPressurePercent);
        Assert.Equal(47.2, observation.PackageWatts);
        Assert.Equal(2150, observation.EffectiveClockMhz);
        Assert.Null(observation.ActiveCores);
        Assert.Null(observation.TotalCores);
        Assert.Null(observation.Actor);
        Assert.Equal(EnvelopeZone.Efficient, observation.Zone);
        Assert.Equal(EnvelopeDecisionKind.None, observation.Decision);
    }

    [Fact]
    public void Update_KeepsOperatingHistoryBoundedToExistingTelemetryHistoryLimit()
    {
        var vm = new DashboardViewModel();
        var start = new DateTimeOffset(2026, 9, 9, 18, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 140; i++)
        {
            var at = start.AddSeconds(i);
            var snapshot = new ControllerSnapshot(PowerState.PowerSaver, "test", false, null, i % 100, 0, null, null, at, [], i, i);
            vm.Update(snapshot, new DashboardTelemetry(30 + i % 5, 1800 + i, at));
        }

        Assert.Equal(120, vm.OperatingHistory.Count);
        Assert.Equal(start.AddSeconds(20), vm.OperatingHistory[0].At);
        Assert.Equal(start.AddSeconds(139), vm.OperatingHistory[^1].At);
    }
}
