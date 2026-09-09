using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ProgressiveDisclosureContractTests
{
    [Fact]
    public void TrajectoryHasLocalHoverLensAndSingleInPlaceDisclosureSurface()
    {
        var root = RepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml.cs"));
        var graph = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Dashboard", "TelemetryGraphControl.xaml.cs"));

        Assert.Contains("x:Name=\"HoverLens\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DisclosurePanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NowButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("QuietDisclosureButton", xaml, StringComparison.Ordinal);
        Assert.Contains("PromoteDisclosureButton", xaml, StringComparison.Ordinal);
        Assert.Contains("DisclosureState", code, StringComparison.Ordinal);
        Assert.Contains("OnTransitionMarkerClicked", code, StringComparison.Ordinal);
        Assert.Contains("HoverChanged", graph, StringComparison.Ordinal);
        Assert.Contains("TrajectoryLensProjection", code, StringComparison.Ordinal);
        Assert.Contains("ReducedMotionOverride", code, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PowerFlow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}
