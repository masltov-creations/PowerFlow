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
    public void TrayHoverPopup_IsAThemedNonActivatingMiniDashboard()
    {
        var root = RepoRoot();
        var trayHost = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Tray", "TrayIconHost.cs"));
        var popupPath = Path.Combine(root, "src", "PowerFlow.App", "Tray", "TrayHoverWindow.xaml");
        var popupCodePath = Path.Combine(root, "src", "PowerFlow.App", "Tray", "TrayHoverWindow.xaml.cs");
        Assert.True(File.Exists(popupPath));
        Assert.True(File.Exists(popupCodePath));
        var popup = File.ReadAllText(popupPath);
        var popupCode = File.ReadAllText(popupCodePath);
        Assert.Contains("CPU", popup, StringComparison.Ordinal);
        Assert.Contains("PACKAGE", popup, StringComparison.Ordinal);
        Assert.Contains("GHz", popup, StringComparison.Ordinal);
        Assert.Contains("NextAction", popup, StringComparison.Ordinal);
        Assert.Contains("TraySparkline", popup, StringComparison.Ordinal);
        Assert.Contains("WS_EX_NOACTIVATE", popupCode, StringComparison.Ordinal);
        Assert.Contains("DashboardTelemetrySession", popupCode, StringComparison.Ordinal);
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
    }
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}
