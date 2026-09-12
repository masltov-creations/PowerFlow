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
    public void ResizeMorphProgress_IsContinuousAcrossTheDensityHysteresisBoundary()
    {
        Assert.Equal(0d, ShellResponsiveDensity.MorphProgress(new ShellLogicalSize(760, 440)), 3);
        Assert.Equal(2d / 3d, ShellResponsiveDensity.MorphProgress(new ShellLogicalSize(860, 520)), 3);
        Assert.Equal(5d / 6d, ShellResponsiveDensity.MorphProgress(new ShellLogicalSize(880, 540)), 3);
        Assert.Equal(1d, ShellResponsiveDensity.MorphProgress(new ShellLogicalSize(900, 560)), 3);
        Assert.True(ShellResponsiveDensity.MorphProgress(new ShellLogicalSize(861, 521)) > ShellResponsiveDensity.MorphProgress(new ShellLogicalSize(860, 520)));
    }

    [Fact]
    public void MainWindowResizeHandler_BlendsCompactAndExpandedPresentationsInsteadOfSnappingAtDensityFlip()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("ApplyResponsiveResizeMorph", code, StringComparison.Ordinal);
        Assert.Contains("ShellResponsiveDensity.MorphProgress", code, StringComparison.Ordinal);
        Assert.Contains("ShellDensity.Compact", code, StringComparison.Ordinal);
        Assert.Contains("ShellDensity.Expanded", code, StringComparison.Ordinal);
        Assert.Contains("ApplyShellGeometryMorph(compactProfile, expandedProfile", code, StringComparison.Ordinal);
        Assert.Contains("ApplyShellTransitionFrame(compactProfile, expandedProfile", code, StringComparison.Ordinal);
    }
    [Fact]
    public void ResizeMorph_AllowsNavigationRailToCollapseContinuouslyToZeroWidth()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("var paneWidth = Math.Max(0d, geometry.NavigationWidth)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Max(NavigationRail.CompactPaneLength, geometry.NavigationWidth)", code, StringComparison.Ordinal);
    }
    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}
