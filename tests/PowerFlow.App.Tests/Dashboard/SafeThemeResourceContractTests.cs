using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class SafeThemeResourceContractTests
{
    [Fact]
    public void DynamicRenderersDoNotIndexThemeResourceDictionariesDirectly()
    {
        var root = RepoRoot();
        var trajectory = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml.cs"));
        var tray = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Tray", "TrayHoverWindow.xaml.cs"));

        Assert.DoesNotContain("Root.Resources[", trajectory, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current.Resources[", trajectory, StringComparison.Ordinal);
        Assert.DoesNotContain("Root.Resources[", tray, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current.Resources[", tray, StringComparison.Ordinal);
    }

    [Fact]
    public void XamlProvidesNamedResolvedBrushSourcesForDynamicDrawing()
    {
        var root = RepoRoot();
        var trajectory = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml"));
        var tray = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Tray", "TrayHoverWindow.xaml"));

        Assert.Contains("x:Name=\"StateBandBrushSource\"", trajectory, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TrayCpuBrushSource\"", tray, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TrayPowerBrushSource\"", tray, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TrayTrackBrushSource\"", tray, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}
