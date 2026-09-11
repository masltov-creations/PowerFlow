using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class WorkloadsSurfaceContractTests
{
    [Fact]
    public void Workloads_ExposeOnlyQualifiedAppImportancePolicy()
    {
        var root = FindRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Settings", "RulesPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Settings", "RulesPage.xaml.cs"));
        var nav = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"));
        Assert.Contains("Workloads", nav, StringComparison.Ordinal);
        Assert.Contains("App importance", xaml, StringComparison.Ordinal);
        Assert.Contains("AppImportance.Low", code, StringComparison.Ordinal);
        Assert.Contains("AppImportance.Normal", code, StringComparison.Ordinal);
        Assert.Contains("AppImportance.High", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceRepeater", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowsServiceCatalog", code, StringComparison.Ordinal);
        Assert.DoesNotContain("advisory", code, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln"))) dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return dir;
    }
}