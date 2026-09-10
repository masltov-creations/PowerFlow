using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class SemanticMorphContractTests
{
    [Fact]
    public void PrimaryAdaptiveComponents_ExposeLocalPresentationMethods()
    {
        var header = Read("src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml.cs");
        var timeline = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");

        Assert.Contains("public void ApplyMorph", header, StringComparison.Ordinal);
        Assert.Contains("public void SetPresentation", timeline, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_DrivesTimelineDensityFromTheExistingBoundsAnimationFrame()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("ApplyShellTransitionFrame", code, StringComparison.Ordinal);
        Assert.Contains("SystemHeaderHost.ApplyMorph", code, StringComparison.Ordinal);
        Assert.Contains("PerformanceTimeline.SetPresentation", code, StringComparison.Ordinal);
        Assert.Contains("ShellMotionPolicy.NavigationProgress", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlContextBandHost", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SecondaryOperationalRow", code, StringComparison.Ordinal);
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
        Assert.Contains("PerformanceTimeline.SetPresentation(to.Timeline)", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}