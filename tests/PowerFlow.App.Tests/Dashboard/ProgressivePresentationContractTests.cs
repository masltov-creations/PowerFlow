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

        foreach (var name in new[] { "Hidden", "Glance", "Compact", "Expanded", "FullScreen" })
            Assert.Contains(name, state, StringComparison.Ordinal);
        Assert.Contains("ShellResizeStateProjection.MinimumHeight", geometry, StringComparison.Ordinal);
        Assert.Contains("(760, 440)", geometry, StringComparison.Ordinal);
        Assert.Contains("(1280, 800)", geometry, StringComparison.Ordinal);
        foreach (var presentation in new[] { "TimelinePresentation.Glance", "TimelinePresentation.Compact", "TimelinePresentation.Expanded", "TimelinePresentation.Full" })
            Assert.Contains(presentation, layout, StringComparison.Ordinal);
        foreach (var depth in new[] { "GovernorControlPresentation.Summary", "GovernorControlPresentation.Bias", "GovernorControlPresentation.Contextual", "GovernorControlPresentation.Deep" })
            Assert.Contains(depth, layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_HasExplicitGrowShrinkControlsAndSameWindowGeometryMotion()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("x:Name=\"PresentationToggleButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AdaptiveControlRegion\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlanceTapTarget\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TransitionToAsync", code, StringComparison.Ordinal);
        Assert.Contains("AnimateShellBoundsAsync", code, StringComparison.Ordinal);
        Assert.Contains("AppWindow.MoveAndResize", code, StringComparison.Ordinal);
        Assert.Contains("ReducedMotionOverride", code, StringComparison.Ordinal);
        Assert.Contains("ShellMotionPolicy.Duration", code, StringComparison.Ordinal);
        Assert.Contains("ApplyShellTransitionFrame", code, StringComparison.Ordinal);
        Assert.Contains("ResolveTargetBounds(PowerFlowShellState.Hidden)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_AdaptsInformationDensityWithoutCreatingAnotherSampler()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");

        Assert.DoesNotContain("MinHeight=\"300\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SetPresentation", code, StringComparison.Ordinal);
        Assert.Contains("TimelinePresentation.Glance", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlanceSummary\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlanceCoreValue\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlanceCoreFill\"", xaml, StringComparison.Ordinal);
        Assert.Contains("UpdateGlanceCoreMeter", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LaneLabelColumn\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Glance_IsDensityOfTheSameShellNotASecondRuntimeWindow()
    {
        var app = Read("src", "PowerFlow.App", "App.xaml.cs");
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.DoesNotContain("new TrayHoverWindow", app, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShellRoot\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PerformanceTimeline\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PowerFlowShellState.Glance", code, StringComparison.Ordinal);
        Assert.Contains("ShellActivationMode.TransientNoActivate", app, StringComparison.Ordinal);
    }

    [Fact]
    public void FullScreen_ExtendsTheSameTimelineAndGovernorArchitecture()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("x:Name=\"PerformanceTimeline\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MachineEnvelopePanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectedActorPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplyShellLayout", code, StringComparison.Ordinal);
        Assert.Contains("PowerFlowShellLayout.Resolve", code, StringComparison.Ordinal);
        Assert.Contains("AppWindowPresenterKind.FullScreen", code, StringComparison.Ordinal);
        Assert.Contains("TimelinePresentation.Full", Read("src", "PowerFlow.App", "Dashboard", "PowerFlowShellLayout.cs"), StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}