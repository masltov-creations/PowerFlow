using PowerFlow.App.Dashboard;
using PowerFlow.App.Tray;
using Windows.Graphics;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellDpiScalingContractTests
{
    [Theory]
    [InlineData(760, 440, 1.25, 950, 550)]
    [InlineData(1280, 800, 1.25, 1600, 1000)]
    [InlineData(320, 176, 1.5, 480, 264)]
    public void LogicalAndPhysicalSizesRoundTrip(int width, int height, double scale, int physicalWidth, int physicalHeight)
    {
        var physical = ShellCoordinateProjection.ToPhysicalSize(width, height, scale);
        Assert.Equal(physicalWidth, physical.Width);
        Assert.Equal(physicalHeight, physical.Height);
        var logical = ShellCoordinateProjection.ToLogicalSize(physical.Width, physical.Height, scale);
        Assert.Equal(width, logical.Width);
        Assert.Equal(height, logical.Height);
    }

    [Fact]
    public void CompactTargetUsesScaledPhysicalBounds()
    {
        var tray = new TrayRect(2200, 1300, 2240, 1340);
        var work = new TrayRect(0, 0, 2560, 1400);
        var target = ShellTransitionGeometry.TargetBounds(tray, work, new RectInt32(1500, 850, 480, 264), PowerFlowShellState.Compact, 1.25);
        Assert.Equal(950, target.Width);
        Assert.Equal(550, target.Height);
    }

    [Fact]
    public void MainWindowResolvesLogicalEndpointsOnceAndMorphsTheExistingShellContinuously()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("var effectiveStart = _motionRenderingAttached ? activeFrame.Bounds : start", code, StringComparison.Ordinal);
        Assert.Contains("var fromLogical = LogicalSize(effectiveStart)", code, StringComparison.Ordinal);
        Assert.Contains("var toLogical = LogicalSize(target)", code, StringComparison.Ordinal);
        Assert.Contains("AppWindow.MoveAndResize(frame.Bounds)", code, StringComparison.Ordinal);
        Assert.Contains("ApplyShellGeometryMorph(_motionFromProfile, _motionToProfile, frame.Sample.Progress)", code, StringComparison.Ordinal);
        Assert.Contains("ApplyShellTransitionFrame(_motionFromProfile, _motionToProfile, frame.ChildSample.Progress", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellTransitionGeometry.Interpolate(start, target, eased)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyShellLayout(layoutState", code, StringComparison.Ordinal);
        Assert.Contains("ShellCoordinateProjection.ToPhysicalSize", code, StringComparison.Ordinal);
    }
    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}
