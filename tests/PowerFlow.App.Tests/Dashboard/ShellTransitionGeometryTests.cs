using PowerFlow.App.Dashboard;
using PowerFlow.App.Tray;
using Windows.Graphics;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellTransitionGeometryTests
{
    [Fact]
    public void TargetBounds_Glance_SitsAboveTrayAndInsideWorkArea()
    {
        var tray = new TrayRect(1800, 1030, 1840, 1070);
        var work = new TrayRect(0, 0, 1920, 1040);
        var target = ShellTransitionGeometry.TargetBounds(tray, work, new RectInt32(0, 0, 1, 1), PowerFlowShellState.Glance);

        Assert.Equal(320, target.Width);
        Assert.Equal(219, target.Height);
        Assert.True(target.X >= work.Left && target.X + target.Width <= work.Right);
        Assert.True(target.Y + target.Height <= tray.Top);
    }

    [Fact]
    public void TargetBounds_Compact_GrowsFromTraySideAndClampsToWorkArea()
    {
        var tray = new TrayRect(1800, 1030, 1840, 1070);
        var work = new TrayRect(0, 0, 1920, 1040);
        var target = ShellTransitionGeometry.TargetBounds(tray, work, new RectInt32(1500, 850, 320, 176), PowerFlowShellState.Compact);

        Assert.Equal(760, target.Width);
        Assert.Equal(440, target.Height);
        Assert.True(target.X >= work.Left && target.X + target.Width <= work.Right);
        Assert.True(target.Y >= work.Top && target.Y + target.Height <= work.Bottom);
    }

    [Fact]
    public void TargetBounds_Expanded_Uses1280x800WhenWorkAreaAllows()
    {
        var tray = new TrayRect(1800, 1030, 1840, 1070);
        var work = new TrayRect(0, 0, 1920, 1040);
        var target = ShellTransitionGeometry.TargetBounds(tray, work, new RectInt32(1160, 600, 760, 440), PowerFlowShellState.Expanded);

        Assert.Equal(1280, target.Width);
        Assert.Equal(800, target.Height);
        Assert.True(target.X >= work.Left && target.X + target.Width <= work.Right);
        Assert.True(target.Y >= work.Top && target.Y + target.Height <= work.Bottom);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void Interpolate_IsMonotonicAndHasExactEndpoints(double progress)
    {
        var start = new RectInt32(1600, 900, 320, 176);
        var end = new RectInt32(700, 300, 760, 440);
        var frame = ShellTransitionGeometry.Interpolate(start, end, progress);

        Assert.InRange(frame.Width, 320, 760);
        Assert.InRange(frame.Height, 176, 440);
        Assert.InRange(frame.X, 700, 1600);
        Assert.InRange(frame.Y, 300, 900);
        if (progress == 0) Assert.Equal(start, frame);
        if (progress == 1) Assert.Equal(end, frame);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(2.0)]
    public void Interpolate_ClampsProgress(double progress)
    {
        var start = new RectInt32(10, 20, 320, 176);
        var end = new RectInt32(100, 200, 760, 440);
        var frame = ShellTransitionGeometry.Interpolate(start, end, progress);

        Assert.Equal(progress < 0 ? start : end, frame);
    }

    [Fact]
    public void TargetBounds_Workspace_Uses1360x860WhenWorkAreaAllowsAndPreservesPinnedCenter()
    {
        var tray = new TrayRect(1800, 1030, 1840, 1070);
        var work = new TrayRect(0, 0, 1920, 1040);
        var current = new RectInt32(300, 120, 1280, 800);
        var target = ShellTransitionGeometry.TargetBounds(tray, work, current, PowerFlowShellState.Workspace);

        Assert.Equal(1360, target.Width);
        Assert.Equal(860, target.Height);
        Assert.InRange(Math.Abs((target.X + target.Width / 2) - (current.X + current.Width / 2)), 0, 1);
        Assert.InRange(Math.Abs((target.Y + target.Height / 2) - (current.Y + current.Height / 2)), 0, 1);
    }

    [Fact]
    public void TargetBounds_ExpandedAfterUserMove_PreservesPinnedCenterInsteadOfReturningToTray()
    {
        var tray = new TrayRect(1800, 1030, 1840, 1070);
        var work = new TrayRect(0, 0, 1920, 1040);
        var current = new RectInt32(580, 300, 760, 440);
        var target = ShellTransitionGeometry.TargetBounds(tray, work, current, PowerFlowShellState.Expanded);

        Assert.InRange(Math.Abs((target.X + target.Width / 2) - (current.X + current.Width / 2)), 0, 1);
        Assert.InRange(Math.Abs((target.Y + target.Height / 2) - (current.Y + current.Height / 2)), 0, 1);
        Assert.True(target.X < 500, "Pinned growth should not snap back toward the tray edge.");
    }

    [Fact]
    public void TargetBounds_Glance_RemainsTrayAnchored()
    {
        var tray = new TrayRect(1800, 1030, 1840, 1070);
        var work = new TrayRect(0, 0, 1920, 1040);
        var moved = new RectInt32(100, 100, 760, 440);
        var target = ShellTransitionGeometry.TargetBounds(tray, work, moved, PowerFlowShellState.Glance);
        Assert.True(target.X > 1400);
        Assert.True(target.Y + target.Height <= tray.Top);
    }
    [Fact]
    public void VisibleTraySeed_AnchorsRealMinimumWindowAboveIcon()
    {
        var tray = new PowerFlow.App.Tray.TrayRect(2200, 1300, 2240, 1340);
        var work = new PowerFlow.App.Tray.TrayRect(0, 0, 2560, 1400);
        var seed = ShellTransitionGeometry.TraySeedBounds(tray, work, 136, 60);
        Assert.Equal(2240 - 136, seed.X);
        Assert.Equal(1300 - 10 - 60, seed.Y);
        Assert.Equal(136, seed.Width);
        Assert.Equal(60, seed.Height);
    }}