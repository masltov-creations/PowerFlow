using Xunit;

namespace PowerFlow.App.Tests.Shell;

public sealed class TrayPopupAndPickerContractTests
{
    [Fact]
    public void AppPicker_IsResponsiveInsteadOfFixedSize()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "PowerFlow.App", "Settings", "RulesPage.xaml"));
        Assert.DoesNotContain("<Grid Width=\"560\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RowDefinition Height=\"280\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"560\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayGlance_IsThemedNonActivatingDensityOfTheUnifiedTimelineShell()
    {
        var root = RepoRoot();
        var trayHost = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Tray", "TrayIconHost.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "App.xaml.cs"));
        var shell = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"));
        var shellCode = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs"));
        var timeline = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml"));

        Assert.Contains("x:Name=\"ShellRoot\"", shell, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PerformanceTimeline\"", shell, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlanceTapTarget\"", shell, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlanceSummary\"", timeline, StringComparison.Ordinal);
        Assert.Contains("WS_EX_NOACTIVATE", shellCode, StringComparison.Ordinal);
        Assert.Contains("TelemetryContinuityRecorder", shellCode, StringComparison.Ordinal);
        Assert.Contains("AcquireVisibility", shellCode, StringComparison.Ordinal);
        Assert.DoesNotContain("DashboardTelemetrySession", shellCode, StringComparison.Ordinal);
        Assert.Contains("PowerFlowShellState.Glance", app, StringComparison.Ordinal);
        Assert.Contains("ShellActivationMode.TransientNoActivate", app, StringComparison.Ordinal);
        Assert.Contains("ShowGlancePreviewAsync", app, StringComparison.Ordinal);
        Assert.Contains("WmMouseMove", trayHost, StringComparison.Ordinal);
        Assert.Contains("Shell_NotifyIconGetRect", trayHost, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayHover_UsesModernCallbacksAndObservedAnchorForOverflowIcons()
    {
        var root = RepoRoot();
        var trayHost = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Tray", "TrayIconHost.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "App.xaml.cs"));

        Assert.Contains("NimSetVersion", trayHost, StringComparison.Ordinal);
        Assert.Contains("NotifyIconVersion4", trayHost, StringComparison.Ordinal);
        Assert.Contains("TryGetHoverAnchorRect", trayHost, StringComparison.Ordinal);
        Assert.Contains("_observedHoverRect", trayHost, StringComparison.Ordinal);
        Assert.Contains("TryGetHoverAnchorRect", app, StringComparison.Ordinal);
        Assert.Contains("ShowGlancePreviewAsync", app, StringComparison.Ordinal);
        Assert.Contains("TryGetIconRect", app, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}