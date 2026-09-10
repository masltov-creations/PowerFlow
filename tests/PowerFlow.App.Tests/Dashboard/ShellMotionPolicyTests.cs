using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellMotionPolicyTests
{
    [Fact]
    public void Duration_UsesFastBoundedGrowthTimings()
    {
        Assert.InRange(ShellMotionPolicy.Duration(PowerFlowShellState.Hidden, PowerFlowShellState.Glance, false).TotalMilliseconds, 110, 150);
        Assert.InRange(ShellMotionPolicy.Duration(PowerFlowShellState.Glance, PowerFlowShellState.Compact, false).TotalMilliseconds, 160, 200);
        Assert.InRange(ShellMotionPolicy.Duration(PowerFlowShellState.Compact, PowerFlowShellState.Expanded, false).TotalMilliseconds, 180, 240);
    }

    [Fact]
    public void Duration_UsesBoundedReverseTimings()
    {
        Assert.InRange(ShellMotionPolicy.Duration(PowerFlowShellState.Expanded, PowerFlowShellState.Compact, false).TotalMilliseconds, 170, 220);
        Assert.InRange(ShellMotionPolicy.Duration(PowerFlowShellState.Compact, PowerFlowShellState.Glance, false).TotalMilliseconds, 150, 200);
        Assert.InRange(ShellMotionPolicy.Duration(PowerFlowShellState.Glance, PowerFlowShellState.Hidden, false).TotalMilliseconds, 110, 160);
    }

    [Fact]
    public void Duration_ReducedMotion_IsImmediate()
        => Assert.Equal(TimeSpan.Zero, ShellMotionPolicy.Duration(PowerFlowShellState.Glance, PowerFlowShellState.Compact, true));

    [Theory]
    [InlineData(PowerFlowShellState.Hidden, PowerFlowShellState.Glance, true)]
    [InlineData(PowerFlowShellState.Glance, PowerFlowShellState.Compact, true)]
    [InlineData(PowerFlowShellState.Compact, PowerFlowShellState.Expanded, true)]
    [InlineData(PowerFlowShellState.Expanded, PowerFlowShellState.Compact, false)]
    [InlineData(PowerFlowShellState.Compact, PowerFlowShellState.Glance, false)]
    public void IsGrowth_TracksDisclosureDirection(PowerFlowShellState from, PowerFlowShellState to, bool expected)
        => Assert.Equal(expected, ShellMotionPolicy.IsGrowth(from, to));

    [Fact]
    public void Ease_GrowthIsCubicEaseOutAndShrinkIsSmoothInOut()
    {
        var growthMid = ShellMotionPolicy.Ease(PowerFlowShellState.Glance, PowerFlowShellState.Compact, 0.5);
        var shrinkMid = ShellMotionPolicy.Ease(PowerFlowShellState.Compact, PowerFlowShellState.Glance, 0.5);
        Assert.InRange(growthMid, 0.84, 0.90);
        Assert.InRange(shrinkMid, 0.45, 0.55);
        Assert.Equal(0, ShellMotionPolicy.Ease(PowerFlowShellState.Glance, PowerFlowShellState.Compact, 0), 6);
        Assert.Equal(1, ShellMotionPolicy.Ease(PowerFlowShellState.Glance, PowerFlowShellState.Compact, 1), 6);
    }

    [Fact]
    public void DetailProgress_RevealsAfterFirstThirdAndHidesEarlyOnShrink()
    {
        Assert.Equal(0, ShellMotionPolicy.DetailProgress(PowerFlowShellState.Glance, PowerFlowShellState.Compact, 0.25), 6);
        Assert.InRange(ShellMotionPolicy.DetailProgress(PowerFlowShellState.Glance, PowerFlowShellState.Compact, 0.5), 0.20, 0.35);
        Assert.Equal(1, ShellMotionPolicy.DetailProgress(PowerFlowShellState.Glance, PowerFlowShellState.Compact, 1), 6);

        Assert.Equal(1, ShellMotionPolicy.DetailProgress(PowerFlowShellState.Expanded, PowerFlowShellState.Compact, 0), 6);
        Assert.InRange(ShellMotionPolicy.DetailProgress(PowerFlowShellState.Expanded, PowerFlowShellState.Compact, 0.25), 0.45, 0.75);
        Assert.Equal(0, ShellMotionPolicy.DetailProgress(PowerFlowShellState.Expanded, PowerFlowShellState.Compact, 0.67), 6);
    }

    [Fact]
    public void Expansion_ReformsModesBeforeNavigationArrives()
    {
        Assert.True(ShellMotionPolicy.ModeMorphProgress(.35) > 0);
        Assert.Equal(0, ShellMotionPolicy.NavigationProgress(.35), 6);
        Assert.True(ShellMotionPolicy.NavigationProgress(.85) > 0);
    }

    [Fact]
    public void Expansion_StagesStatsThenContextAfterPrimaryMotionBegins()
    {
        Assert.True(ShellMotionPolicy.PrimaryAnchorProgress(.20) > 0);
        Assert.True(ShellMotionPolicy.StatsMorphProgress(.30) > 0);
        Assert.Equal(0, ShellMotionPolicy.ContextMorphProgress(.30), 6);
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
