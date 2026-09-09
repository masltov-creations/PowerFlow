using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PrimaryAnchorComponentTests
{
    [Fact]
    public void PowerModeControl_ExposesOneDensityAwareControlSurface()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "PowerModeControl.xaml.cs");
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PowerModeControl.xaml");

        Assert.Contains("ModePresentation Presentation", code, StringComparison.Ordinal);
        Assert.Contains("ModeRequested", code, StringComparison.Ordinal);
        Assert.Contains("CurrentChipLayer", xaml, StringComparison.Ordinal);
        Assert.Contains("SegmentedLayer", xaml, StringComparison.Ordinal);
        Assert.Contains("CardsLayer", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FontSize=\"10\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellHeaderControl_HasMinimalCompactAndSystemPresentations()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml.cs");
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml");

        Assert.Contains("HeaderPresentation Presentation", code, StringComparison.Ordinal);
        Assert.Contains("MinimalHeader", xaml, StringComparison.Ordinal);
        Assert.Contains("CompactHeader", xaml, StringComparison.Ordinal);
        Assert.Contains("SystemHeader", xaml, StringComparison.Ordinal);
        Assert.Contains("IsPreviewMode", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TrajectoryControl_AcceptsSemanticPresentation()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml.cs");
        Assert.Contains("SetShellPresentation(TrajectoryPresentation", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_DispatchesModeRequestsThroughOneControllerPath()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("OnPowerModeRequested", code, StringComparison.Ordinal);
        Assert.Contains("PowerModeChoice.PowerSaver", code, StringComparison.Ordinal);
        Assert.Contains("PowerModeChoice.Balanced", code, StringComparison.Ordinal);
        Assert.Contains("PowerModeChoice.Performance", code, StringComparison.Ordinal);
        Assert.Contains("PowerModeChoice.Auto", code, StringComparison.Ordinal);
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
