using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class InspectionHoverContractTests
{
    [Fact]
    public void Timeline_HasIndependentHoverLayerSharedInspectionAndDoesNotMutateSelectionOnMove()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        Assert.Contains("x:Name=\"HoverLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("InspectionExplanationProjection.ForObservation", code, StringComparison.Ordinal);
        Assert.Contains("InspectionExplanationProjection.ForPolicyHandle", code, StringComparison.Ordinal);
        Assert.Contains("SetHoveredObservationIndices", code, StringComparison.Ordinal);
        var move = Slice(code, "private void OnPointerMoved", "private void OnPointerExited");
        Assert.DoesNotContain("_selectedObservationIndices =", move, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionChanged?.Invoke", move, StringComparison.Ordinal);
    }

    [Fact]
    public void Atlas_HasTransientHoverLayerHoverCardAndHoverEventWithoutSelectionMutation()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml.cs");
        Assert.Contains("x:Name=\"AtlasHoverLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AtlasHoverCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PointerMoved=\"OnPointerMoved\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PointerExited=\"OnPointerExited\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HoverChanged", code, StringComparison.Ordinal);
        Assert.Contains("InspectionExplanationProjection.ForAtlasCell", code, StringComparison.Ordinal);
        Assert.Contains("SetHoveredObservationIndices", code, StringComparison.Ordinal);
        var move = Slice(code, "private void OnPointerMoved", "private void OnPointerExited");
        Assert.DoesNotContain("_selectedObservationIndices =", move, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionChanged?.Invoke", move, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_CrossLinksTransientHoverWithoutReusingPersistentSelection()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("PerformanceTimeline.CursorChanged += OnTimelineCursorChanged", code, StringComparison.Ordinal);
        Assert.Contains("PerformanceAtlas.HoverChanged += OnAtlasHoverChanged", code, StringComparison.Ordinal);
        Assert.Contains("PerformanceAtlas.SetHoveredObservationIndices", code, StringComparison.Ordinal);
        Assert.Contains("PerformanceTimeline.SetHoveredObservationIndices", code, StringComparison.Ordinal);
        var timelineHover = Slice(code, "private void OnTimelineCursorChanged", "private void OnAtlasHoverChanged");
        Assert.DoesNotContain("UpdateAnalyticalSelection", timelineHover, StringComparison.Ordinal);
    }

    [Fact]
    public void HoverTransitions_RespectResolvedReducedMotionWithoutAnimatingTelemetryTraces()
    {
        var timeline = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        var atlas = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml.cs");
        var main = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("SetReducedMotion(bool", timeline, StringComparison.Ordinal);
        Assert.Contains("SetReducedMotion(bool", atlas, StringComparison.Ordinal);
        Assert.Contains("PerformanceTimeline.SetReducedMotion(!ShouldAnimatePresentation())", main, StringComparison.Ordinal);
        Assert.Contains("PerformanceAtlas.SetReducedMotion(!ShouldAnimatePresentation())", main, StringComparison.Ordinal);
        Assert.Contains("DoubleAnimation", timeline, StringComparison.Ordinal);
        Assert.Contains("DoubleAnimation", atlas, StringComparison.Ordinal);
        Assert.DoesNotContain("TraceLayer", Slice(timeline, "private void SetTransient", "private void"), StringComparison.Ordinal);
    }
    private static string Slice(string text, string start, string end)
    {
        var a = text.IndexOf(start, StringComparison.Ordinal);
        if (a < 0) return string.Empty;
        var b = text.IndexOf(end, a + start.Length, StringComparison.Ordinal);
        return b < 0 ? text[a..] : text[a..b];
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}
