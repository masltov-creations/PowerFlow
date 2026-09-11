using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class WorkloadsSurfaceContractTests
{
    [Fact]
    public void Workloads_ExposeServiceImportanceAndBoostWithoutUnsafeSharedHostActuation()
    {
        var root = FindRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Settings", "RulesPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Settings", "RulesPage.xaml.cs"));
        var nav = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"));
        Assert.Contains("Workloads", nav, StringComparison.Ordinal);
        Assert.Contains("ServiceRepeater", xaml, StringComparison.Ordinal);
        Assert.Contains("ServiceImportanceBox", xaml, StringComparison.Ordinal);
        Assert.Contains("ServiceBoostBox", xaml, StringComparison.Ordinal);
        Assert.Contains("SHARED HOST", code, StringComparison.Ordinal);
        Assert.Contains("service-specific actuation remains advisory", code, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WindowsServiceCatalog", code, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln"))) dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return dir;
    }
}