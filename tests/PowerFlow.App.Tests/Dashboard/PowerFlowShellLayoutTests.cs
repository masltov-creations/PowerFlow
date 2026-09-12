using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PowerFlowShellLayoutTests
{
    [Theory]
    [InlineData(PowerFlowShellState.Glance, NavigationPresentation.None, HeaderPresentation.Minimal, TimelinePresentation.Glance, GovernorControlPresentation.Summary)]
    [InlineData(PowerFlowShellState.Compact, NavigationPresentation.Overlay, HeaderPresentation.Compact, TimelinePresentation.Compact, GovernorControlPresentation.Bias)]
    [InlineData(PowerFlowShellState.Expanded, NavigationPresentation.Rail, HeaderPresentation.System, TimelinePresentation.Expanded, GovernorControlPresentation.Contextual)]
    [InlineData(PowerFlowShellState.FullScreen, NavigationPresentation.Rail, HeaderPresentation.System, TimelinePresentation.Full, GovernorControlPresentation.Deep)]
    public void Resolve_MapsStateToAdaptivePresentations(
        PowerFlowShellState state,
        NavigationPresentation navigation,
        HeaderPresentation header,
        TimelinePresentation timeline,
        GovernorControlPresentation governorControls)
    {
        var profile = PowerFlowShellLayout.Resolve(
            state == PowerFlowShellState.Glance ? 320 : state == PowerFlowShellState.Compact ? 760 : 1280,
            state == PowerFlowShellState.Glance ? 176 : state == PowerFlowShellState.Compact ? 440 : 800,
            state,
            "flow");

        Assert.Equal(navigation, profile.Navigation);
        Assert.Equal(header, profile.Header);
        Assert.Equal(timeline, profile.Timeline);
        Assert.Equal(governorControls, profile.GovernorControls);
    }

    [Fact]
    public void Compact_PreservesTimelineAndHighValueBiasControl()
    {
        var profile = PowerFlowShellLayout.Resolve(760, 440, PowerFlowShellState.Compact, "flow");
        Assert.Equal(TimelinePresentation.Compact, profile.Timeline);
        Assert.Equal(GovernorControlPresentation.Bias, profile.GovernorControls);
        Assert.True(profile.Geometry.ControlBandHeight > 0);
    }

    [Fact]
    public void Expanded_PrioritizesTimelineWithNarrowRailAndContextualControls()
    {
        var profile = PowerFlowShellLayout.Resolve(1280, 800, PowerFlowShellState.Expanded, "flow");
        Assert.Equal(TimelinePresentation.Expanded, profile.Timeline);
        Assert.Equal(GovernorControlPresentation.Contextual, profile.GovernorControls);
        Assert.True(profile.Geometry.NavigationWidth >= 126);
        Assert.True(profile.Geometry.ControlBandHeight >= 108);
    }

    [Fact]
    public void Expanded_RemainsFluidInsideSameSemanticState()
    {
        var small = PowerFlowShellLayout.Resolve(980, 620, PowerFlowShellState.Expanded, "flow");
        var large = PowerFlowShellLayout.Resolve(1320, 820, PowerFlowShellState.Expanded, "flow");

        Assert.Equal(PowerFlowShellState.Expanded, small.State);
        Assert.Equal(PowerFlowShellState.Expanded, large.State);
        Assert.True(large.Geometry.ContentPadding > small.Geometry.ContentPadding);
        Assert.True(large.Geometry.Gap > small.Geometry.Gap);
        Assert.True(large.Geometry.NavigationWidth > small.Geometry.NavigationWidth);
        Assert.True(large.Geometry.ControlBandHeight >= small.Geometry.ControlBandHeight);
    }

    [Fact]
    public void NonLiveSectionsAtCompactRequestExpandedSemanticDensity()
    {
        foreach (var section in new[] { "rules", "model", "tune", "settings" })
        {
            var profile = PowerFlowShellLayout.Resolve(760, 440, PowerFlowShellState.Compact, section);
            Assert.Equal(PowerFlowShellState.Compact, profile.State);
            Assert.Equal(NavigationPresentation.Overlay, profile.Navigation);
        }
    }
}
