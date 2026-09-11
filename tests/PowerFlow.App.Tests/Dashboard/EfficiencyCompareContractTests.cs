using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class EfficiencyCompareContractTests
{
    [Fact]
    public void MainWindow_ExposesCompareAsAStableNavigationSection()
    {
        var xaml = File.ReadAllText(Source("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"));
        var code = File.ReadAllText(Source("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs"));

        Assert.Contains("Content=\"Compare\" Tag=\"compare\"", xaml);
        Assert.Contains("EfficiencyCompareControl x:Name=\"EfficiencyComparePanel\"", xaml);
        Assert.Contains("EfficiencyComparePanel.Visibility = tag == \"compare\"", code);
    }

    [Fact]
    public void CompareSurface_OffersFiveMinuteThroughFiveHourABCollectionAndDisclosesProxy()
    {
        var xaml = File.ReadAllText(Source("src", "PowerFlow.App", "Dashboard", "EfficiencyCompareControl.xaml"));

        Assert.Contains("5 MIN", xaml);
        Assert.Contains("30 MIN", xaml);
        Assert.Contains("5 HR", xaml);
        Assert.Contains("START BASELINE", xaml);
        Assert.Contains("START AFTER", xaml);
        Assert.Contains("Nominal full-CPU equivalent minutes", xaml);
        Assert.Contains("It is not an application job counter", xaml);
        Assert.Contains("Recording does not change power policy", xaml);
    }

    [Fact]
    public void AppOwnsComparisonRuntimeSoCollectionCanContinueWithoutDashboard()
    {
        var app = File.ReadAllText(Source("src", "PowerFlow.App", "App.xaml.cs"));

        Assert.Contains("private readonly EfficiencyExperimentRuntime _efficiencyExperimentRuntime = new();", app);
        Assert.Contains("_efficiencyExperimentRuntime.Observe(_telemetryRecorder.History);", app);
        Assert.Contains("ApplyOperatingModeAsync, _efficiencyExperimentRuntime", app);
    }

    private static string Source(params string[] relative)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var marker = Path.Combine(directory, "PowerFlow.sln");
            if (File.Exists(marker)) return Path.Combine(new[] { directory }.Concat(relative).ToArray());
            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }
        throw new DirectoryNotFoundException("PowerFlow source root not found.");
    }
}