using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class MachineBaselineSurfaceContractTests
{
    [Fact]
    public void Profile_surface_exposes_one_click_baseline_curves_idle_results_and_recommendation()
    {
        var root = RepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "CpuCapabilityProfileControl.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "CpuCapabilityProfileControl.xaml.cs"));

        Assert.Contains("BASELINE MACHINE", xaml);
        Assert.Contains("30 MIN", xaml);
        Assert.Contains("BaselineProgressBar", xaml);
        Assert.Contains("ComparisonMetricBox", xaml);
        Assert.Contains("BaselineComparisonChart", xaml);
        Assert.Contains("IdleComparisonRepeater", xaml);
        Assert.Contains("RecommendationText", xaml);
        Assert.Contains("KNEE", xaml);
        Assert.Contains("MachineBaselineSessionRuntime", code);
        Assert.Contains("MachineBaselineComparisonRun", code);
    }

    [Fact]
    public void App_owns_baseline_session_and_main_window_only_attaches_to_it()
    {
        var root = RepoRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "App.xaml.cs"));
        var main = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs"));

        Assert.Contains("MachineBaselineSessionRuntime", app);
        Assert.Contains("PowerFlowMachineBaselineHost", app);
        Assert.Contains("CancelAndWaitAsync", app);
        Assert.Contains("EffectiveMachineBaselineRuns", main);
        Assert.Contains("SaveMachineBaselineRunAsync", app);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}
