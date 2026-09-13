using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PerformanceTimelineContractTests
{
    [Fact]
    public void Timeline_IsOneSharedInstrumentWithThreeVisualRowsAndFourAlignedDataSeries()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");

        Assert.Contains("x:Name=\"TimelineRoot\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GridLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TraceLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CursorLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PolicyValueLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PolicyTimeLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PolicyHandleLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ActorDecisionLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("COMPUTE PRESSURE", xaml, StringComparison.Ordinal);
        Assert.Contains("POWER / CPU PERF", xaml, StringComparison.Ordinal);
        Assert.Contains("CAPACITY / CORES", xaml, StringComparison.Ordinal);
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

    [Fact]
    public void Timeline_UsesWeightedCompactLanesAndExplicitFocusRanges()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        var projection = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineProjection.cs");

        Assert.Contains("Height=\"1.5*\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LaneWeights = [1.4d, 1.1d, 1.5d]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_plotHeight / 4d", code, StringComparison.Ordinal);
        Assert.Contains("DomainMin", projection, StringComparison.Ordinal);
        Assert.Contains("minimumSpan: 40d", projection, StringComparison.Ordinal);
        Assert.Contains("minimumSpan: 30d", projection, StringComparison.Ordinal);
        Assert.Contains("100d - _data.Lanes[2].DomainMin", code, StringComparison.Ordinal);
        Assert.Contains("CPU performance overlays package power", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_CoalescesTelemetryUpdatesAndCachesCoreProjectionWork()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        Assert.Contains("IsCoreTimelineRelevant", code, StringComparison.Ordinal);
        Assert.Contains("_coreProjectionDirty", code, StringComparison.Ordinal);
        Assert.Contains("RefreshCoreTimelines();", code, StringComparison.Ordinal);
        Assert.Contains("_observations.SequenceEqual(incoming)", code, StringComparison.Ordinal);
        Assert.Contains("if (projectionChanged || envelopeChanged) RequestRedraw();", code, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateLiveLabels();\n        Redraw();", code.Replace("\r\n", "\n"), StringComparison.Ordinal);
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

        foreach (var name in new[] { "CpuTracePath", "PowerTracePath", "ClockTracePath" })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CoreThreadLayer\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Polyline", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new Polyline", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PlotCanvas.Children.Clear()", code, StringComparison.Ordinal);
        Assert.Contains("RectangleGeometry", code, StringComparison.Ordinal);
        Assert.Contains("ShapePreservingCurve.Build", code, StringComparison.Ordinal);
        Assert.Contains("_plotWidth * 10d / Math.Max(1d, _windowSeconds)", code, StringComparison.Ordinal);
    }    [Fact]
    public void Timeline_ClipsAndRepositionsTransientVisualsAcrossResize()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        Assert.Contains("TimelinePlotHost.Clip = new RectangleGeometry", code, StringComparison.Ordinal);
        Assert.Contains("e.NewSize.Width", code, StringComparison.Ordinal);
        Assert.Contains("if (_draggingPolicyHandle is null) HideCursor()", code, StringComparison.Ordinal);
        Assert.Contains("RedrawLinkedHover();", code, StringComparison.Ordinal);
        Assert.Contains("_resizeRedrawTimer.Stop()", code, StringComparison.Ordinal);
    }
}
