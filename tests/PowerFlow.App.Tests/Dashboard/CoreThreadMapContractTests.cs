using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class CoreThreadMapContractTests
{
    [Fact]
    public void Timeline_UsesStackedCoreHistoryInsteadOfLatestStateMatrix()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        var main = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("x:Name=\"CoreThreadLayer\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"CoresTracePath\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SetCoreThreadHistory", code, StringComparison.Ordinal);
        Assert.Contains("DrawCoreHistoryHistogram", code, StringComparison.Ordinal);
        Assert.Contains("PerformanceTimeline.SetCoreThreadHistory(_recorder.History)", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Spec_LocksSixteenCoreTimeSlicesAndGracefulCurveSplit()
    {
        var spec = Read("docs", "design", "core-thread-map-v1.md");
        Assert.Contains("One visual column represents one telemetry time slice", spec, StringComparison.Ordinal);
        Assert.Contains("all physical cores are stacked vertically", spec, StringComparison.Ordinal);
        Assert.Contains("Missing utilization must never be promoted to Active", spec, StringComparison.Ordinal);
        Assert.Contains("no separate aggregate core line", spec, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}
