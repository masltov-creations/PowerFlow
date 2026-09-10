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
        Assert.Contains("x:Name=\"PlotCanvas\"", xaml, StringComparison.Ordinal);
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
}
