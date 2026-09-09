using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class DashboardLayoutTests
{
    [Fact]
    public void MainDashboard_IsTrajectoryFirstAndEveryMajorVisualHasMeaning()
    {
        var xaml = File.ReadAllText(RepoFile("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"));
        var trajectory = File.ReadAllText(RepoFile("src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml"));

        Assert.Contains("TrajectoryControl", xaml);
        Assert.Contains("TelemetryGraphControl", trajectory);
        Assert.Contains("AutoNode", trajectory);
        Assert.Contains("SaverNode", trajectory);
        Assert.Contains("BalancedNode", trajectory);
        Assert.Contains("PerformanceNode", trajectory);
        Assert.Contains("NowText", trajectory);
        Assert.DoesNotContain("RuleFlowControl", xaml);
        Assert.DoesNotContain("DecisionPressureControl", xaml);
        Assert.DoesNotContain("StateRailControl", xaml);
        Assert.DoesNotContain("FlowFieldControl", xaml);
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
