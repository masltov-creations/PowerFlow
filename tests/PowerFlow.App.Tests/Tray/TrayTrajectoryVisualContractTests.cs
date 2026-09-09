using Xunit;

namespace PowerFlow.App.Tests.Tray;

public sealed class TrayTrajectoryVisualContractTests
{
    [Fact]
    public void GlanceIsLevelZeroOfTheSharedTrajectoryRatherThanASeparateSparklineWindow()
    {
        var root = RepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "App.xaml"));

        Assert.Contains("dash:TrajectoryControl", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompactTelemetryStrip\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlanceTapTarget\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TrajectoryProjection.Create", code, StringComparison.Ordinal);
        Assert.Contains("_recorder.History", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TrayMiniTrajectoryProjection", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DashboardTelemetrySession", code, StringComparison.Ordinal);
        Assert.Contains("AcquireVisibility", code, StringComparison.Ordinal);
        Assert.Contains("WS_EX_NOACTIVATE", code, StringComparison.Ordinal);
        Assert.Contains("PowerFlowCpuBrush", app, StringComparison.Ordinal);
        Assert.Contains("PowerFlowStateSaverBrush", app, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}
