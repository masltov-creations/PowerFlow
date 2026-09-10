using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class SemanticMorphContractTests
{
    [Fact]
    public void PrimaryCockpitComponents_ExposeLocalMorphMethods()
    {
        var root = RepoRoot();
        foreach (var file in new[]
        {
            "ShellHeaderControl.xaml.cs",
            "PowerModeControl.xaml.cs",
            "LiveStatsControl.xaml.cs",
            "ControlContextControl.xaml.cs",
            "TrajectoryControl.xaml.cs"
        })
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", file));
            Assert.Contains("public void ApplyMorph", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MainWindow_DrivesLocalMorphsFromTheExistingBoundsAnimationFrame()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("ApplyShellTransitionFrame", code, StringComparison.Ordinal);
        Assert.Contains("PowerModeBandHost.ApplyMorph", code, StringComparison.Ordinal);
        Assert.Contains("LiveStatsHost.ApplyMorph", code, StringComparison.Ordinal);
        Assert.Contains("ControlContextBandHost.ApplyMorph", code, StringComparison.Ordinal);
        Assert.Contains("Trajectory.ApplyMorph", code, StringComparison.Ordinal);
        Assert.Contains("ShellMotionPolicy.NavigationProgress", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Morphing_DoesNotCrossfadeTheWholeDashboard()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.DoesNotContain("ShellRoot.Opacity", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CockpitRoot.Opacity", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CockpitSurface.Opacity", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReducedMotion_SkipsInteriorInterpolationAndAppliesTargetLayout()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("reducedMotion", code, StringComparison.Ordinal);
        Assert.Contains("ApplyShellLayout(toState", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}
