using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class DashboardLayoutTests
{
    [Fact]
    public void MainDashboard_IsGraphFirstAndEveryMajorVisualHasMeaning()
    {
        var xaml = File.ReadAllText(RepoFile("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"));

        Assert.Contains("TelemetryGraphControl", xaml);
        Assert.Contains("RuleFlowControl", xaml);
        Assert.Contains("DecisionPressureControl", xaml);
        Assert.Contains("StateRailControl", xaml);
        Assert.Contains("CPU + PACKAGE POWER", xaml);
        Assert.DoesNotContain("FlowFieldControl", xaml);
        Assert.DoesNotContain("Value=\"{Binding Activity}\"", File.ReadAllText(RepoFile("src", "PowerFlow.App", "Dashboard", "RuleFlowControl.xaml")));
    }

    [Fact]
    public void DedicatedRulesView_UsesVisualRuleCardsInsteadOfAFlatTable()
    {
        var xaml = File.ReadAllText(RepoFile("src", "PowerFlow.App", "Settings", "RulesPage.xaml"));

        Assert.Contains("RuleCardBorder", xaml);
        Assert.Contains("PERFORMANCE LOCK", xaml);
        Assert.Contains("BALANCED", xaml);
        Assert.DoesNotContain("Grid.ColumnDefinitions><ColumnDefinition Width=\"220\"", xaml);
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
        {
            var parent = Directory.GetParent(dir) ?? throw new DirectoryNotFoundException("PowerFlow repo root not found");
            dir = parent.FullName;
        }
        return Path.Combine(new[] { dir }.Concat(parts).ToArray());
    }
}
