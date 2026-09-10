using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class CoreThreadMapContractTests
{
    [Fact]
    public void Timeline_UsesCoreStateHistoryRibbonInsteadOfLatestOnlyMatrixOrGenericTrace()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        var main = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("CORE STATE", xaml, StringComparison.Ordinal);
        foreach (var name in new[] { "CoreActivePath", "CoreAwakePath", "CoreParkedPath" })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CoresTracePath", xaml, StringComparison.Ordinal);
        Assert.Contains("SetCoreThreadHistory", code, StringComparison.Ordinal);
        Assert.Contains("DrawCoreStateTimeline", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DrawCoreThreadMap", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CoreThreadLayer.Children.Add", code, StringComparison.Ordinal);
        Assert.Contains("PerformanceTimeline.SetCoreThreadHistory(_recorder.History)", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Spec_LocksStateCountsAcrossTimeAndBoundedRenderer()
    {
        var spec = Read("docs", "design", "core-thread-map-v1.md");
        Assert.Contains("state count, not by physical-core identity", spec, StringComparison.Ordinal);
        Assert.Contains("Active at the bottom, Awake-idle above it, Parked above that", spec, StringComparison.Ordinal);
        Assert.Contains("three persistent Path elements", spec, StringComparison.Ordinal);
        Assert.Contains("Missing utilization must never be promoted to Active", spec, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}
