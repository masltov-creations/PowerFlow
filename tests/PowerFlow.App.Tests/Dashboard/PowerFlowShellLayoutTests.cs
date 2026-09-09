using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PowerFlowShellLayoutTests
{
    [Theory]
    [InlineData(PowerFlowShellState.Glance,
        NavigationPresentation.None, HeaderPresentation.Minimal,
        ModePresentation.CurrentChip, StatsPresentation.Inline,
        TrajectoryPresentation.Minimal, ControlContextPresentation.CauseLine,
        SecondaryPresentation.Hidden)]
    [InlineData(PowerFlowShellState.Compact,
        NavigationPresentation.Overlay, HeaderPresentation.Compact,
        ModePresentation.Segmented, StatsPresentation.CompactRail,
        TrajectoryPresentation.Compact, ControlContextPresentation.Rail,
        SecondaryPresentation.Hidden)]
    [InlineData(PowerFlowShellState.Expanded,
        NavigationPresentation.Rail, HeaderPresentation.System,
        ModePresentation.Cards, StatsPresentation.FullRail,
        TrajectoryPresentation.Full, ControlContextPresentation.Modules,
        SecondaryPresentation.Full)]
    [InlineData(PowerFlowShellState.FullScreen,
        NavigationPresentation.Rail, HeaderPresentation.System,
        ModePresentation.Cards, StatsPresentation.FullRail,
        TrajectoryPresentation.Full, ControlContextPresentation.Modules,
        SecondaryPresentation.Full)]
    public void Resolve_MapsStateToSemanticPresentations(
        PowerFlowShellState state,
        NavigationPresentation navigation,
        HeaderPresentation header,
        ModePresentation modes,
        StatsPresentation stats,
        TrajectoryPresentation trajectory,
        ControlContextPresentation context,
        SecondaryPresentation secondary)
    {
        var profile = PowerFlowShellLayout.Resolve(
            state == PowerFlowShellState.Glance ? 320 : state == PowerFlowShellState.Compact ? 760 : 1280,
            state == PowerFlowShellState.Glance ? 176 : state == PowerFlowShellState.Compact ? 440 : 800,
            state,
            "flow");

        Assert.Equal(navigation, profile.Navigation);
        Assert.Equal(header, profile.Header);
        Assert.Equal(modes, profile.Modes);
        Assert.Equal(stats, profile.Stats);
        Assert.Equal(trajectory, profile.Trajectory);
        Assert.Equal(context, profile.ControlContext);
        Assert.Equal(secondary, profile.Secondary);
    }

    [Fact]
    public void Compact_PreservesStatsAndControlContext()
    {
        var profile = PowerFlowShellLayout.Resolve(760, 440, PowerFlowShellState.Compact, "flow");
        Assert.Equal(StatsPresentation.CompactRail, profile.Stats);
        Assert.Equal(ControlContextPresentation.Rail, profile.ControlContext);
    }

    [Fact]
    public void Expanded_UsesReferenceAsymmetricPrimarySplit()
    {
        var profile = PowerFlowShellLayout.Resolve(1280, 800, PowerFlowShellState.Expanded, "flow");
        Assert.InRange(profile.Geometry.PrimaryGraphFraction, 0.68, 0.72);
        Assert.True(profile.Geometry.NavigationWidth >= 126);
        Assert.True(profile.Geometry.ControlBandHeight > 0);
        Assert.True(profile.Geometry.SecondaryBandHeight > 0);
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
    }

    [Fact]
    public void RulesAndSettingsAtCompactRequestExpandedSemanticDensity()
    {
        var rules = PowerFlowShellLayout.Resolve(760, 440, PowerFlowShellState.Compact, "rules");
        var settings = PowerFlowShellLayout.Resolve(760, 440, PowerFlowShellState.Compact, "settings");

        Assert.Equal(PowerFlowShellState.Expanded, rules.State);
        Assert.Equal(PowerFlowShellState.Expanded, settings.State);
        Assert.Equal(NavigationPresentation.Rail, rules.Navigation);
        Assert.Equal(NavigationPresentation.Rail, settings.Navigation);
    }
}
