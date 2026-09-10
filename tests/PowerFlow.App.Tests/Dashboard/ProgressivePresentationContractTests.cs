using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ProgressivePresentationContractTests
{
    [Fact]
    public void Shell_DefinesConnectedCanonicalPresentationStatesAndSizes()
    {
        var state = Read("src", "PowerFlow.App", "Dashboard", "PowerFlowShellState.cs");
        var layout = Read("src", "PowerFlow.App", "Dashboard", "PowerFlowShellLayout.cs");
        var geometry = Read("src", "PowerFlow.App", "Dashboard", "ShellTransitionGeometry.cs");

        Assert.Contains("Hidden", state, StringComparison.Ordinal);
        Assert.Contains("Glance", state, StringComparison.Ordinal);
        Assert.Contains("Compact", state, StringComparison.Ordinal);
        Assert.Contains("Expanded", state, StringComparison.Ordinal);
        Assert.Contains("FullScreen", state, StringComparison.Ordinal);
        Assert.Contains("(320, 176)", geometry, StringComparison.Ordinal);
        Assert.Contains("(760, 440)", geometry, StringComparison.Ordinal);
        Assert.Contains("(1280, 800)", geometry, StringComparison.Ordinal);
        Assert.Contains("NavigationPresentation.Rail", layout, StringComparison.Ordinal);
        Assert.Contains("StatsPresentation.CompactRail", layout, StringComparison.Ordinal);
        Assert.Contains("ControlContextPresentation.Rail", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_HasExplicitGrowShrinkControlsAndSameWindowGeometryMotion()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("x:Name=\"PresentationToggleButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ControlContextBandHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlanceTapTarget\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TransitionToAsync", code, StringComparison.Ordinal);
        Assert.Contains("AnimateShellBoundsAsync", code, StringComparison.Ordinal);
        Assert.Contains("AppWindow.MoveAndResize", code, StringComparison.Ordinal);
        Assert.Contains("ReducedMotionOverride", code, StringComparison.Ordinal);
        Assert.Contains("ShellMotionPolicy.Duration", code, StringComparison.Ordinal);
        Assert.Contains("ApplyShellTransitionFrame", code, StringComparison.Ordinal);
        Assert.Contains("ShellMotionPolicy.ModeMorphProgress", code, StringComparison.Ordinal);
        Assert.Contains("ResolveTargetBounds(PowerFlowShellState.Hidden)", code, StringComparison.Ordinal);
        Assert.Contains("await AnimateShellBoundsAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Trajectory_AdaptsItsInformationDensityInsteadOfForcingExpandedHeight()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml.cs");

        Assert.DoesNotContain("MinHeight=\"300\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SetShellPresentation", code, StringComparison.Ordinal);
        Assert.Contains("SetLayoutProfile", code, StringComparison.Ordinal);
        Assert.Contains("Graph.MinHeight", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Glance_IsDensityOfTheSameShellNotASecondRuntimeWindow()
    {
        var app = Read("src", "PowerFlow.App", "App.xaml.cs");
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.DoesNotContain("new TrayHoverWindow", app, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellRoot\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PowerFlowShellState.Glance", code, StringComparison.Ordinal);
        Assert.Contains("ShellActivationMode.TransientNoActivate", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_ReflowsAndSupportsARealFullScreenExtension()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var trajectory = Read("src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml.cs");

        Assert.Contains("x:Name=\"LiveStatsHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ControlContextBandHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SecondaryOperationalRow\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplyShellLayout", code, StringComparison.Ordinal);
        Assert.Contains("PowerFlowShellLayout.Resolve", code, StringComparison.Ordinal);
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
