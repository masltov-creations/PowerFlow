using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellMotionPolicyTests
{
    [Fact]
    public void Duration_UsesFastBoundedGrowthTimings()
    {
        Assert.InRange(ShellMotionPolicy.Duration(PowerFlowShellState.Hidden, PowerFlowShellState.Glance, false).TotalMilliseconds, 220, 300);
        Assert.InRange(ShellMotionPolicy.Duration(PowerFlowShellState.Glance, PowerFlowShellState.Compact, false).TotalMilliseconds, 220, 320);
        Assert.InRange(ShellMotionPolicy.Duration(PowerFlowShellState.Compact, PowerFlowShellState.Expanded, false).TotalMilliseconds, 280, 420);
    }

    [Fact]
    public void Duration_UsesBoundedReverseTimings()
    {
        Assert.InRange(ShellMotionPolicy.Duration(PowerFlowShellState.Expanded, PowerFlowShellState.Compact, false).TotalMilliseconds, 260, 380);
        Assert.InRange(ShellMotionPolicy.Duration(PowerFlowShellState.Compact, PowerFlowShellState.Glance, false).TotalMilliseconds, 200, 300);
        Assert.InRange(ShellMotionPolicy.Duration(PowerFlowShellState.Glance, PowerFlowShellState.Hidden, false).TotalMilliseconds, 110, 160);
    }

    [Fact]
    public void Duration_ReducedMotion_IsImmediate()
        => Assert.Equal(TimeSpan.Zero, ShellMotionPolicy.Duration(PowerFlowShellState.Glance, PowerFlowShellState.Compact, true));


    [Fact]
    public void DurationForTravel_GivesSizeMorphsEnoughTimeToSettleSmoothly()
    {
        var hoverToCompact = ShellMotionPolicy.DurationForTravel(PowerFlowShellState.Glance, PowerFlowShellState.Compact, false, 493d);
        var compactToExpanded = ShellMotionPolicy.DurationForTravel(PowerFlowShellState.Compact, PowerFlowShellState.Expanded, false, 632d);

        Assert.InRange(hoverToCompact.TotalMilliseconds, 430d, 470d);
        Assert.InRange(compactToExpanded.TotalMilliseconds, 500d, 540d);
    }
    [Fact]
    public void DurationForTravel_ScalesLargeWindowMorphsWithoutBecomingSluggish()
    {
        var medium = ShellMotionPolicy.DurationForTravel(PowerFlowShellState.Compact, PowerFlowShellState.Expanded, false, 640);
        var large = ShellMotionPolicy.DurationForTravel(PowerFlowShellState.FullScreen, PowerFlowShellState.Compact, false, 1325);
        Assert.InRange(medium.TotalMilliseconds, 420, 540);
        Assert.InRange(large.TotalMilliseconds, 1000, 1100);
        Assert.True(large > medium);
    }
    [Theory]
    [InlineData(PowerFlowShellState.Hidden, PowerFlowShellState.Glance, true)]
    [InlineData(PowerFlowShellState.Glance, PowerFlowShellState.Compact, true)]
    [InlineData(PowerFlowShellState.Compact, PowerFlowShellState.Expanded, true)]
    [InlineData(PowerFlowShellState.Expanded, PowerFlowShellState.Compact, false)]
    [InlineData(PowerFlowShellState.Compact, PowerFlowShellState.Glance, false)]
    public void IsGrowth_TracksDisclosureDirection(PowerFlowShellState from, PowerFlowShellState to, bool expected)
        => Assert.Equal(expected, ShellMotionPolicy.IsGrowth(from, to));

    [Fact]
    public void Ease_UsesSoftSmootherStepInBothDirections()
    {
        var growthMid = ShellMotionPolicy.Ease(PowerFlowShellState.Glance, PowerFlowShellState.Compact, 0.5);
        var shrinkMid = ShellMotionPolicy.Ease(PowerFlowShellState.Compact, PowerFlowShellState.Glance, 0.5);
        Assert.InRange(growthMid, 0.45, 0.55);
        Assert.InRange(shrinkMid, 0.45, 0.55);
        Assert.Equal(0, ShellMotionPolicy.Ease(PowerFlowShellState.Glance, PowerFlowShellState.Compact, 0), 6);
        Assert.Equal(1, ShellMotionPolicy.Ease(PowerFlowShellState.Glance, PowerFlowShellState.Compact, 1), 6);
    }

    [Fact]
    public void DetailProgress_IsContinuousAndMonotonicInBothDirections()
    {
        Assert.InRange(ShellMotionPolicy.DetailProgress(PowerFlowShellState.Glance, PowerFlowShellState.Compact, 0.25), 0.01, 0.20);
        Assert.InRange(ShellMotionPolicy.DetailProgress(PowerFlowShellState.Glance, PowerFlowShellState.Compact, 0.5), 0.35, 0.65);
        Assert.Equal(1, ShellMotionPolicy.DetailProgress(PowerFlowShellState.Glance, PowerFlowShellState.Compact, 1), 6);

        Assert.Equal(1, ShellMotionPolicy.DetailProgress(PowerFlowShellState.Expanded, PowerFlowShellState.Compact, 0), 6);
        Assert.InRange(ShellMotionPolicy.DetailProgress(PowerFlowShellState.Expanded, PowerFlowShellState.Compact, 0.25), 0.70, 0.95);
        Assert.InRange(ShellMotionPolicy.DetailProgress(PowerFlowShellState.Expanded, PowerFlowShellState.Compact, 0.67), 0.01, 0.35);
    }

    [Fact]
    public void Expansion_MorphsModesAndNavigationWithinTheSameContinuousWindow()
    {
        Assert.True(ShellMotionPolicy.ModeMorphProgress(.35) > 0);
        Assert.True(ShellMotionPolicy.NavigationProgress(.35) > 0);
        Assert.True(ShellMotionPolicy.NavigationProgress(.85) > 0);
    }

    [Fact]
    public void Expansion_MorphsPrimaryStatsAndContextAsOneContinuousDisclosure()
    {
        Assert.True(ShellMotionPolicy.PrimaryAnchorProgress(.20) > 0);
        Assert.True(ShellMotionPolicy.StatsMorphProgress(.30) > 0);
        Assert.True(ShellMotionPolicy.ContextMorphProgress(.30) > 0);
        Assert.True(ShellMotionPolicy.ContextMorphProgress(.60) > 0);
    }

    [Fact]
    public void Collapse_RemovesNavigationBeforePrimaryAnchors()
    {
        Assert.True(ShellMotionPolicy.NavigationProgress(.30, collapsing: true) <
                    ShellMotionPolicy.PrimaryAnchorProgress(.30, collapsing: true));
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(0d)]
    [InlineData(.5d)]
    [InlineData(1d)]
    [InlineData(2d)]
    public void SemanticProgressFunctions_AreBounded(double t)
    {
        foreach (var value in new[]
        {
            ShellMotionPolicy.ModeMorphProgress(t),
            ShellMotionPolicy.StatsMorphProgress(t),
            ShellMotionPolicy.ContextMorphProgress(t),
            ShellMotionPolicy.NavigationProgress(t),
            ShellMotionPolicy.NavigationProgress(t, collapsing: true),
            ShellMotionPolicy.PrimaryAnchorProgress(t),
            ShellMotionPolicy.PrimaryAnchorProgress(t, collapsing: true)
        })
            Assert.InRange(value, 0d, 1d);
    }}
