using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class CoreLoadIntensityContractTests
{
    [Fact]
    public void CoreTimeline_UsesPersistentLoadBandPathsRatherThanPerCellControls()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");

        foreach (var name in new[] { "CoreActivePath", "CoreActiveMediumPath", "CoreActiveHighPath", "CoreActiveHotPath", "CoreAwakePath", "CoreParkedPath" })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("AddLoadCells", code, StringComparison.Ordinal);
        Assert.Contains("load >= 75", code, StringComparison.Ordinal);
        Assert.Contains("load >= 50", code, StringComparison.Ordinal);
        Assert.Contains("load >= 25", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CoreThreadLayer.Children.Add", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}