using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PressureContextContractTests
{
    [Fact]
    public void Timeline_ShowsActualPressureWithThresholdContextWithoutInPlotTextClutter()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        Assert.Contains("x:Name=\"CpuPressureLabel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CpuThresholdText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DrawPressureContext", code, StringComparison.Ordinal);
        Assert.Contains("PressureZoneProjection.Build", code, StringComparison.Ordinal);
        Assert.Contains("Model-zone pressure uses current CPU utilization", code, StringComparison.Ordinal);
        Assert.Contains("if (!_tuneMode && rail.Metric == PerformanceTimelineMetric.CpuPressure) continue", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}