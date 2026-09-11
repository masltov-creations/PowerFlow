using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class CapacityOverlayContractTests
{
    [Fact]
    public void Timeline_ShowsRequestedAndDeliveredCapacityWithoutAddingPerSampleControls()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs"));
        Assert.Contains("CAPACITY / CORES", xaml);
        Assert.Contains("RequestedCapacityPath", xaml);
        Assert.Contains("DeliveredCapacityPath", xaml);
        var start = code.IndexOf("private void DrawCapacityOverlay", StringComparison.Ordinal);
        var end = code.IndexOf("private static void AddLoadCells", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var overlayMethod = code[start..end];
        Assert.DoesNotContain("new Rectangle {", overlayMethod);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "PowerFlow.App"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("PowerFlow repo root not found.");
    }
}