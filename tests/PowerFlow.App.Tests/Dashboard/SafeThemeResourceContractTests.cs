using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class SafeThemeResourceContractTests
{
    [Fact]
    public void DynamicRenderersDoNotIndexThemeResourceDictionariesDirectly()
    {
        var root = RepoRoot();
        var trajectory = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml.cs"));
        var graph = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "TelemetryGraphControl.xaml.cs"));

        Assert.DoesNotContain("Root.Resources[", trajectory, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current.Resources[", trajectory, StringComparison.Ordinal);
        Assert.DoesNotContain("Resources[", graph, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current.Resources[", graph, StringComparison.Ordinal);
    }

    [Fact]
    public void UnifiedShellUsesThemeResourcesAndNamedTrajectoryBrushSource()
    {
        var root = RepoRoot();
        var trajectory = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml"));
        var shell = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"));
        var app = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "App.xaml"));

        Assert.Contains("x:Name=\"StateBandBrushSource\"", trajectory, StringComparison.Ordinal);
        Assert.Contains("PowerFlowSurfaceBrush", shell, StringComparison.Ordinal);
        Assert.Contains("PowerFlowCanvasBrush", app, StringComparison.Ordinal);
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
