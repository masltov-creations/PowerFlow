using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ProgressivePresentationContractTests
{
    [Fact]
    public void Shell_DefinesThreeConnectedCanonicalPresentationSizes()
    {
        var main = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var tray = Read("src", "PowerFlow.App", "Tray", "TrayHoverWindow.xaml.cs");

        Assert.Contains("CompressedWidth = 760", main, StringComparison.Ordinal);
        Assert.Contains("CompressedHeight = 440", main, StringComparison.Ordinal);
        Assert.Contains("ExpandedWidth = 1120", main, StringComparison.Ordinal);
        Assert.Contains("ExpandedHeight = 720", main, StringComparison.Ordinal);
        Assert.Contains("SizeInt32(CompressedWidth, CompressedHeight)", main, StringComparison.Ordinal);
        Assert.Contains("PopupWidth = 320", tray, StringComparison.Ordinal);
        Assert.Contains("PopupHeight = 176", tray, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_HasExplicitCompressedExpandedEvolutionControls()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("x:Name=\"PresentationToggleButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExpandedContextRail\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplyPresentationMode", code, StringComparison.Ordinal);
        Assert.Contains("AnimateWindowTo", code, StringComparison.Ordinal);
        Assert.Contains("ReducedMotionOverride", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Trajectory_AdaptsItsInformationDensityInsteadOfForcingExpandedHeight()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml.cs");

        Assert.DoesNotContain("MinHeight=\"300\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SetLayoutProfile", code, StringComparison.Ordinal);
        Assert.Contains("Graph.MinHeight", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Hover_IsAGlanceNotASecondDashboard()
    {
        var xaml = Read("src", "PowerFlow.App", "Tray", "TrayHoverWindow.xaml");
        Assert.Contains("x:Name=\"CompactTelemetryLine\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.Row=\"1\" ColumnSpacing=\"16\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RowDefinition Height=\"96\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_ReflowsAndSupportsARealFullScreenState()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var trajectory = Read("src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml.cs");

        Assert.Contains("x:Name=\"CompactTelemetryStrip\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TelemetryCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FullScreenContext\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveLayout", code, StringComparison.Ordinal);
        Assert.Contains("AppWindowPresenterKind.FullScreen", code, StringComparison.Ordinal);
        Assert.Contains("SetLayoutProfile", trajectory, StringComparison.Ordinal);
    }
    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}