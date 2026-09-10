using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ResponsiveRulesLayoutTests
{
    [Fact]
    public void RulesPage_ReflowsCardsAndDoesNotDependOnSelectedRows()
    {
        var xaml = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml");
        var code = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml.cs");
        Assert.Contains("x:Name=\"RulesRepeater\"", xaml);
        Assert.Contains("UniformGridLayout", xaml);
        Assert.Contains("MinItemWidth", xaml);
        Assert.Contains("ItemsStretch=\"Fill\"", xaml);
        Assert.Contains("Add app rule", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AppPickerDialog", xaml);
        Assert.DoesNotContain("Capture foreground", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MaxWidth=\"1080\"", xaml);
        Assert.DoesNotContain("Orientation=\"Horizontal\" Spacing=\"9\"", xaml);
        Assert.DoesNotContain("RulesList.SelectedItem", code);
        Assert.Contains("OnEditEntitlementFromCard", code);
        Assert.Contains("PerformanceEntitlement", code);
        Assert.Contains("CeilingLabel", xaml);
        Assert.Contains("TimingLabel", xaml);
        Assert.Contains("OnRemoveFromCard", code);
    }

    [Fact]
    public void Dashboard_CompactStateUsesCanonicalGeometry()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var geometry = Read("src", "PowerFlow.App", "Dashboard", "ShellTransitionGeometry.cs");
        Assert.Contains("PowerFlowShellState.Compact", code);
        Assert.Contains("(760, 440)", geometry);
        Assert.DoesNotContain("SizeInt32(1180, 760)", code);
    }

    private static string Read(params string[] parts) => File.ReadAllText(RepoFile(parts));

    private static string RepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException("PowerFlow repo root not found");
        return Path.Combine(new[] { dir }.Concat(parts).ToArray());
    }
}
