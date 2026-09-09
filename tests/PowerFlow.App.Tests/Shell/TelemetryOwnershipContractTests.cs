using Xunit;

namespace PowerFlow.App.Tests.Shell;

public sealed class TelemetryOwnershipContractTests
{
    [Fact]
    public void AppOwnsSingleContinuityRecorderAndSurfacesUseVisibilityLeases()
    {
        var root = RepoRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "App.xaml.cs"));
        var main = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs"));
        var tray = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Tray", "TrayHoverWindow.xaml.cs"));

        Assert.Contains("TelemetryContinuityRecorder? _telemetryRecorder", app, StringComparison.Ordinal);
        Assert.Contains("new TelemetryContinuityRecorder", app, StringComparison.Ordinal);
        Assert.Contains("UpdateControllerSnapshot(snapshot)", app, StringComparison.Ordinal);
        Assert.Contains("await _telemetryRecorder.StartAsync()", app, StringComparison.Ordinal);
        Assert.Contains("new MainWindow(_controller, _telemetryRecorder", app, StringComparison.Ordinal);
        Assert.Contains("new TrayHoverWindow(_controller, _telemetryRecorder", app, StringComparison.Ordinal);

        Assert.Contains("TelemetryContinuityRecorder recorder", main, StringComparison.Ordinal);
        Assert.Contains("AcquireVisibility", main, StringComparison.Ordinal);
        Assert.DoesNotContain("new DashboardTelemetrySession", main, StringComparison.Ordinal);
        Assert.DoesNotContain("new DashboardTelemetrySource", main, StringComparison.Ordinal);

        Assert.Contains("TelemetryContinuityRecorder recorder", tray, StringComparison.Ordinal);
        Assert.Contains("AcquireVisibility", tray, StringComparison.Ordinal);
        Assert.DoesNotContain("new DashboardTelemetrySession", tray, StringComparison.Ordinal);
        Assert.DoesNotContain("new DashboardTelemetrySource", tray, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}
