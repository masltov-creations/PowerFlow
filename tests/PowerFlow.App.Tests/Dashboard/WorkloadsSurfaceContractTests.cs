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
        var app = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "App.xaml.cs"));
        Assert.Contains("Workloads", nav, StringComparison.Ordinal);
        Assert.Contains("App importance", xaml, StringComparison.Ordinal);
        Assert.Contains("AppImportance.Low", code, StringComparison.Ordinal);
        Assert.Contains("AppImportance.Normal", code, StringComparison.Ordinal);
        Assert.Contains("AppImportance.High", code, StringComparison.Ordinal);
        Assert.Contains("ULTRA", xaml, StringComparison.Ordinal);
        Assert.Contains("ComboBoxItem Content=\"Ultra\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ComboBoxItem Content=\"ULTRA", xaml, StringComparison.Ordinal);
        Assert.Contains("ULTRA · FORCE WHILE ACTIVE", xaml, StringComparison.Ordinal);
        Assert.Contains("AppRuleMode.Ultra", code, StringComparison.Ordinal);
        Assert.Contains("UpdateAppTriggeredUltraProfile", app, StringComparison.Ordinal);
        Assert.Contains("_powerModeProfileRuntime.Apply(PowerFlowOperatingProfiles.Ultra", app, StringComparison.Ordinal);
        Assert.Contains("_powerModeProfileRuntime.Restore(", app, StringComparison.Ordinal);
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