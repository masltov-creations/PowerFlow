using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class CoreThreadMapContractTests
{
    [Fact]
    public void Timeline_UsesDedicatedCoreThreadMatrixInsteadOfFourthGenericTrace()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        var main = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("x:Name=\"CoreThreadLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CoreHistoryPath\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"CoresTracePath\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SetCoreThreadState", code, StringComparison.Ordinal);
        Assert.Contains("DrawCoreThreadMap", code, StringComparison.Ordinal);
        Assert.Contains("PerformanceTimeline.SetCoreThreadState(_recorder.LatestRichTelemetry?.LogicalProcessors)", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Spec_LocksDiscreteSquareMapAndGracefulCurveSplit()
    {
        var spec = Read("docs", "design", "core-thread-map-v1.md");
        Assert.Contains("One visual column represents one physical core", spec, StringComparison.Ordinal);
        Assert.Contains("Missing utilization must never be promoted to Active", spec, StringComparison.Ordinal);
        Assert.Contains("thin, low-emphasis, smooth history strip", spec, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}
