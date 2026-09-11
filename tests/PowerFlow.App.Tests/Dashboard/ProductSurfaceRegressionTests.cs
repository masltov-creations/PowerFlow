using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ProductSurfaceRegressionTests
{
    [Fact]
    public void ProductionNavigation_IsExactlyLiveWorkloadsBaselinePlusSettings()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("ms-appx:///Assets/PowerFlow.png", xaml, StringComparison.Ordinal);
        Assert.Contains("AppWindow.SetIcon(iconPath)", code, StringComparison.Ordinal);
        Assert.Contains("Content=\"Live\" Tag=\"flow\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Workloads\" Tag=\"rules\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Baseline\" Tag=\"profile\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Settings\" Tag=\"settings\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Tune Auto\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Model\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Compare\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovedProductSurfaces_AreNotHiddenInProductionSource()
    {
        var root = RepoRoot();
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var rulesXaml = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml");
        var rulesCode = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml.cs");

        Assert.False(File.Exists(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "EfficiencyCompareControl.xaml")));
        Assert.False(File.Exists(Path.Combine(root, "src", "PowerFlow.App", "Controller", "EfficiencyExperimentRuntime.cs")));
        Assert.DoesNotContain("PerformanceAtlas", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("EfficiencyCompare", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("TuneControlRegion", xaml + code, StringComparison.Ordinal);
        Assert.DoesNotContain("ServicePolicy", rulesXaml + rulesCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AdaptiveActuationToggle", rulesXaml + rulesCode, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumZoneBox", rulesXaml + rulesCode, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkloadsAndSettings_ExposeOnlyCurrentProductChoices()
    {
        var rules = Read("src", "PowerFlow.App", "Settings", "RulesPage.xaml");
        var settings = Read("src", "PowerFlow.App", "Settings", "SettingsPage.xaml");
        var settingsCode = Read("src", "PowerFlow.App", "Settings", "SettingsPage.xaml.cs");

        Assert.Contains("LOW · EFFICIENCY ONLY", rules, StringComparison.Ordinal);
        Assert.Contains("NORMAL · QUALIFIED BOOST", rules, StringComparison.Ordinal);
        Assert.Contains("HIGH · FAST BOOST", rules, StringComparison.Ordinal);
        Assert.Contains("Theme", settings, StringComparison.Ordinal);
        Assert.Contains("Reduced motion", settings, StringComparison.Ordinal);
        Assert.Contains("Start with Windows", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerPlanInfo", settingsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AdaptiveActuationEnabled = true", settingsCode, StringComparison.Ordinal);
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(path).ToArray()));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PowerFlow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate PowerFlow repo root.");
    }
}