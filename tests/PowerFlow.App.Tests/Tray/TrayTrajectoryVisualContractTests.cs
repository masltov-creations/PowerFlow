using Xunit;

namespace PowerFlow.App.Tests.Tray;

public sealed class TrayTrajectoryVisualContractTests
{
    [Fact]
    public void TrayPopupIsLevelZeroTrajectoryRatherThanLocalSparklineCard()
    {
        var root = RepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Tray", "TrayHoverWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Tray", "TrayHoverWindow.xaml.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "App.xaml"));

        Assert.Contains("TrayTrajectoryCanvas", xaml, StringComparison.Ordinal);
        Assert.Contains("SaverNode", xaml, StringComparison.Ordinal);
        Assert.Contains("BalancedNode", xaml, StringComparison.Ordinal);
        Assert.Contains("PerformanceNode", xaml, StringComparison.Ordinal);
        Assert.Contains("TrayNowLabel", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LIVE WHILE OPEN", xaml, StringComparison.Ordinal);
        Assert.Contains("TrayMiniTrajectoryProjection.Create", code, StringComparison.Ordinal);
        Assert.Contains("_recorder.History", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly List<(DateTimeOffset At, double Cpu)> _samples", code, StringComparison.Ordinal);
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
