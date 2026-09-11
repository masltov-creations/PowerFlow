using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class CpuProfileSurfaceContractTests
{
    [Fact]
    public void Profile_IsUserTriggeredPersistentAndMeasurementOnly()
    {
        var root = FindRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "CpuCapabilityProfileControl.xaml"));
        var profiler = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Controller", "CpuCapabilityProfiler.cs"));
        var main = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"));
        Assert.Contains("Content=\"Baseline\" Tag=\"profile\"", main, StringComparison.Ordinal);
        Assert.Contains("BASELINE MACHINE", xaml, StringComparison.Ordinal);
        Assert.Contains("PROFILE CURRENT MODE", xaml, StringComparison.Ordinal);
        Assert.Contains("1 / 2 / 4 / 8 / 16-thread", xaml, StringComparison.Ordinal);
        Assert.Contains("CpuCapabilityAnalysis.Build", profiler, StringComparison.Ordinal);
        Assert.DoesNotContain("SetManualStateAsync", profiler, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyOperatingMode", profiler, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln"))) dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return dir;
    }
}
