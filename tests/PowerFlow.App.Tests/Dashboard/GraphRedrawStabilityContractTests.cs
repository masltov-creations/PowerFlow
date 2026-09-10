using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class GraphRedrawStabilityContractTests
{
    [Fact]
    public void Timeline_CoalescesResizePresentationAndThemeRedrawRequests()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        Assert.Contains("private bool _redrawQueued", code, StringComparison.Ordinal);
        Assert.Contains("private bool _presentationApplied", code, StringComparison.Ordinal);
        Assert.Contains("if (_presentationApplied && Presentation == presentation) return;", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue", code, StringComparison.Ordinal);
        Assert.Contains("OnSizeChanged(object sender, SizeChangedEventArgs e) => RequestRedraw();", code, StringComparison.Ordinal);
        Assert.Contains("ActualThemeChanged += (_, _) => RequestRedraw();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Atlas_CoalescesResizeAndThemeRedrawRequests()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml.cs");
        Assert.Contains("private bool _redrawQueued", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue", code, StringComparison.Ordinal);
        Assert.Contains("if (_initialized) RequestRedraw();", code, StringComparison.Ordinal);
        Assert.Contains("ActualThemeChanged += (_, _) => RequestRedraw();", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}