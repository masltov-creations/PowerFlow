using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class DashboardCompactnessTests
{
    [Fact]
    public void MainDashboard_DefaultWindowIsCompactAndDoesNotScroll()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs"));
        var trajectory = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml"));

        Assert.Contains("SizeInt32(CompressedWidth, CompressedHeight)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"390\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TrajectoryControl", xaml, StringComparison.Ordinal);
        Assert.Contains("Range60Button", trajectory, StringComparison.Ordinal);
        Assert.Contains("Range120Button", trajectory, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardViewModel_RetainsTwoMinutesOfTelemetry()
    {
        var vm = new DashboardViewModel();
        vm.Configure(PowerFlowConfig.Default);
        var start = new DateTimeOffset(2026, 9, 8, 20, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < 130; i++)
        {
            var at = start.AddSeconds(i);
            var snapshot = new ControllerSnapshot(PowerState.Balanced, "test", false, null, i % 100, 0, null, null, at, [], i, i);
            vm.Update(snapshot, new DashboardTelemetry(45 + i % 20, 3800, at));
        }

        Assert.Equal(120, vm.Samples.Count);
        Assert.Equal(start.AddSeconds(10), vm.Samples[0].At);
        Assert.Equal(start.AddSeconds(129), vm.Samples[^1].At);
    }

    [Fact]
    public void TelemetryProjection_FindsNearestSampleForHoverWithinSelectedWindow()
    {
        var latest = new DateTimeOffset(2026, 9, 8, 20, 2, 0, TimeSpan.Zero);
        var samples = new[]
        {
            new DashboardSample(latest.AddSeconds(-120), 10, 40, 3500, PowerState.PowerSaver),
            new DashboardSample(latest.AddSeconds(-60), 30, 55, 3900, PowerState.Balanced),
            new DashboardSample(latest.AddSeconds(-30), 50, 70, 4200, PowerState.Balanced),
            new DashboardSample(latest, 20, 48, 3700, PowerState.Balanced),
        };

        var picked = TelemetryPlotProjection.FindNearestSample(samples, 0.75, latest, 120);

        Assert.NotNull(picked);
        Assert.Equal(latest.AddSeconds(-30), picked!.At);
        Assert.Equal(50, picked.CpuPercent);
    }

    [Fact]
    public void TransitionProjection_MapsRecentStateChangeIntoSelectedGraphWindow()
    {
        var latest = new DateTimeOffset(2026, 9, 8, 20, 2, 0, TimeSpan.Zero);
        var transition = new TransitionRecord(latest.AddSeconds(-30), PowerState.PowerSaver, PowerState.Balanced, "CPU demand sustained", true);

        var marker = TelemetryPlotProjection.NormalizedTransitionX(transition, latest, 60);

        Assert.NotNull(marker);
        Assert.Equal(0.5, marker!.Value, 2);
        Assert.Null(TelemetryPlotProjection.NormalizedTransitionX(transition, latest, 20));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("PowerFlow repo root not found.");
    }
}
