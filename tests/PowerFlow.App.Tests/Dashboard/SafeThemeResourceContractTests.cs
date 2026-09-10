using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class SafeThemeResourceContractTests
{
    [Fact]
    public void DynamicTimelineRenderer_DoesNotIndexThemeResourceDictionariesDirectly()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        Assert.DoesNotContain("Root.Resources[", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current.Resources[", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Resources[", code, StringComparison.Ordinal);
    }

    [Fact]
    public void UnifiedShellAndTimeline_UsePowerFlowThemeResources()
    {
        var timeline = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var shell = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var app = Read("src", "PowerFlow.App", "App.xaml");

        Assert.Contains("PowerFlowPanelBrush", timeline, StringComparison.Ordinal);
        Assert.Contains("PowerFlowCardBorderBrush", timeline, StringComparison.Ordinal);
        Assert.Contains("PowerFlowSurfaceBrush", shell, StringComparison.Ordinal);
        Assert.Contains("PowerFlowCanvasBrush", app, StringComparison.Ordinal);
        Assert.Contains("PowerFlowCpuBrush", app, StringComparison.Ordinal);
        Assert.Contains("PowerFlowPowerBrush", app, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        if (dir is null) throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray()));
    }
}