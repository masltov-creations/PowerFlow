using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Policy;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class TrajectoryDashboardContractTests
{
    [Fact]
    public void ViewModelCanHydratePreOpenContinuityImmediately()
    {
        var t0 = new DateTimeOffset(2026, 9, 8, 23, 0, 0, TimeSpan.Zero);
        var history = new[]
        {
            new ContinuitySample(t0, 8, 48, 3200, PowerState.PowerSaver, "quiet", false, null, .1, null),
            new ContinuitySample(t0.AddSeconds(5), 38, 72, 4100, PowerState.Balanced, "load", false, null, .8, "compiler.exe")
        };
        var snapshot = new ControllerSnapshot(PowerState.Balanced, "load", false, null, 38, .8, null, "compiler.exe", t0.AddSeconds(5), Array.Empty<TransitionRecord>(), 0, 0);
        var vm = new DashboardViewModel();

        vm.UpdateContinuity(snapshot, history, new DashboardTelemetry(72, 4100, t0.AddSeconds(5)));

        Assert.Equal(2, vm.Samples.Count);
        Assert.Equal(PowerState.PowerSaver, vm.Samples[0].State);
        Assert.Equal(PowerState.Balanced, vm.Samples[1].State);
        Assert.Equal("72.0 W", vm.WattsLabel);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}
