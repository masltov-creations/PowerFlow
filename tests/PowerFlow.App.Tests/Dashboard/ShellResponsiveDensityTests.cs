using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellResponsiveDensityTests
{
    [Fact]
    public void Compact_StaysCompactUntilBothExpandedThresholdsAreCrossed()
    {
        Assert.Equal(ShellDensity.Compact, ShellResponsiveDensity.Resolve(new ShellLogicalSize(899, 559), ShellDensity.Compact));
        Assert.Equal(ShellDensity.Compact, ShellResponsiveDensity.Resolve(new ShellLogicalSize(900, 559), ShellDensity.Compact));
        Assert.Equal(ShellDensity.Compact, ShellResponsiveDensity.Resolve(new ShellLogicalSize(899, 560), ShellDensity.Compact));
        Assert.Equal(ShellDensity.Expanded, ShellResponsiveDensity.Resolve(new ShellLogicalSize(900, 560), ShellDensity.Compact));
    }

    [Fact]
    public void Expanded_UsesHysteresisAndDoesNotFlapNearBoundary()
    {
        Assert.Equal(ShellDensity.Expanded, ShellResponsiveDensity.Resolve(new ShellLogicalSize(880, 540), ShellDensity.Expanded));
        Assert.Equal(ShellDensity.Compact, ShellResponsiveDensity.Resolve(new ShellLogicalSize(860, 540), ShellDensity.Expanded));
        Assert.Equal(ShellDensity.Compact, ShellResponsiveDensity.Resolve(new ShellLogicalSize(880, 520), ShellDensity.Expanded));
    }

    [Fact]
    public void Layout_CanUseExpandedDensityWithoutChangingRequestedShellState()
    {
        var profile = PowerFlowShellLayout.Resolve(1100, 700, PowerFlowShellState.Compact, "flow", ShellDensity.Expanded);

        Assert.Equal(PowerFlowShellState.Compact, profile.State);
        Assert.Equal(NavigationPresentation.Rail, profile.Navigation);
        Assert.Equal(HeaderPresentation.System, profile.Header);
        Assert.Equal(TimelinePresentation.Expanded, profile.Timeline);
        Assert.Equal(GovernorControlPresentation.Contextual, profile.GovernorControls);
    }

    [Fact]
    public void NonLiveSectionForcesExpandedDensityWithoutChangingRequestedState()
    {
        var profile = PowerFlowShellLayout.Resolve(760, 440, PowerFlowShellState.Compact, "model", ShellDensity.Compact);

        Assert.Equal(PowerFlowShellState.Compact, profile.State);
        Assert.Equal(NavigationPresentation.Rail, profile.Navigation);
        Assert.Equal(TimelinePresentation.Expanded, profile.Timeline);
    }

    [Fact]
    public void MainWindowResizeHandler_UpdatesDensityButNeverInfersShellStateFromSize()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("_layoutDensity", code, StringComparison.Ordinal);
        Assert.Contains("ShellResponsiveDensity.Resolve", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_shellState = logical.Width >=", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (profile.State != state && state == PowerFlowShellState.Compact) _shellState", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowResizeHandler_UsesOneStableSemanticDensityPerResizeFrame()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("QueueResizeReflow", code, StringComparison.Ordinal);
        Assert.Contains("ShellResponsiveDensity.Resolve", code, StringComparison.Ordinal);
        Assert.Contains("ApplyShellLayout(_shellState, width, height)", code, StringComparison.Ordinal);
        Assert.Contains("ResetSemanticMorphPresentation(stableProfile)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyResponsiveResizeMorph", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellResponsiveDensity.MorphProgress", code, StringComparison.Ordinal);
    }
    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}
