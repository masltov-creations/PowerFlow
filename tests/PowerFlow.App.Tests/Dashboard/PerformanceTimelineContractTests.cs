using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PerformanceTimelineContractTests
{
    [Fact]
    public void Timeline_IsOneSharedInstrumentWithFourAlignedKpiLanesAndPolicyLayers()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");

        Assert.Contains("x:Name=\"TimelineRoot\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GridLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TraceLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CursorLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EnvelopeRailLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ActorDecisionLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CPU PRESSURE", xaml, StringComparison.Ordinal);
        Assert.Contains("PACKAGE POWER", xaml, StringComparison.Ordinal);
        Assert.Contains("EFFECTIVE CLOCK", xaml, StringComparison.Ordinal);
        Assert.Contains("CORES AWAKE", xaml, StringComparison.Ordinal);
        Assert.Contains("PerformanceTimelineProjection.Build", code, StringComparison.Ordinal);
        Assert.Contains("FindNearestObservationIndex", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_DoesNotUseTinyExplicitTypography()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        foreach (var size in new[] { "8", "9", "10" })
            Assert.DoesNotContain($"FontSize=\"{size}\"", xaml, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }

    [Fact]
    public void TimelineRenderer_UsesPersistentClippedPathsInsteadOfRebuildingPolylines()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");

        foreach (var name in new[] { "CpuTracePath", "PowerTracePath", "ClockTracePath", "CoresTracePath" })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Polyline", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new Polyline", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PlotCanvas.Children.Clear()", code, StringComparison.Ordinal);
        Assert.Contains("RectangleGeometry", code, StringComparison.Ordinal);
        Assert.Contains("ShapePreservingCurve.Build", code, StringComparison.Ordinal);
    }}
