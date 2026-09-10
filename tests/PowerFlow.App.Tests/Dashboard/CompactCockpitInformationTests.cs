using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class CompactCockpitInformationTests
{
    [Fact]
    public void Compact_KeepsAllFiveEssentialAnchors()
    {
        var profile = PowerFlowShellLayout.Resolve(760, 440, PowerFlowShellState.Compact, "flow");
        Assert.Equal(HeaderPresentation.Compact, profile.Header);
        Assert.Equal(ModePresentation.Segmented, profile.Modes);
        Assert.Equal(StatsPresentation.CompactRail, profile.Stats);
        Assert.Equal(TrajectoryPresentation.Compact, profile.Trajectory);
        Assert.Equal(ControlContextPresentation.Rail, profile.ControlContext);
        Assert.Equal(SecondaryPresentation.Hidden, profile.Secondary);
    }

    [Fact]
    public void Compact_UsesTheSameAnchorInstancesAsExpanded()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        Assert.Equal(1, Count(xaml, "<dash:ShellHeaderControl"));
        Assert.Equal(1, Count(xaml, "<dash:PowerModeControl"));
        Assert.Equal(1, Count(xaml, "<dash:TrajectoryControl"));
        Assert.Equal(1, Count(xaml, "<dash:LiveStatsControl"));
        Assert.Equal(1, Count(xaml, "<dash:ControlContextControl"));
    }

    [Fact]
    public void Compact_HeaderOwnsNavigationAffordanceInsteadOfPermanentPane()
    {
        var header = Read("src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml");
        var headerCode = Read("src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml.cs");
        var mainCode = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("x:Name=\"CompactNavigationButton\"", header, StringComparison.Ordinal);
        Assert.Contains("RULES / APPS", header, StringComparison.Ordinal);
        Assert.Contains("SETTINGS", header, StringComparison.Ordinal);
        Assert.Contains("RulesRequested", headerCode, StringComparison.Ordinal);
        Assert.Contains("SettingsRequested", headerCode, StringComparison.Ordinal);
        Assert.Contains("OnHeaderRulesRequested", mainCode, StringComparison.Ordinal);
        Assert.Contains("OnHeaderSettingsRequested", mainCode, StringComparison.Ordinal);
        Assert.Contains("NavigationPresentation.Overlay", mainCode, StringComparison.Ordinal);
        Assert.Contains("NavigationRail.IsPaneVisible = false", mainCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Compact_DoesNotCollapseStatsOrControlContext()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.DoesNotContain("LiveStatsHost.Visibility = Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlContextBandHost.Visibility = Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.Contains("LiveStatsHost.Presentation = profile.Stats", code, StringComparison.Ordinal);
        Assert.Contains("ControlContextBandHost.Presentation = profile.ControlContext", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Compact_StaysAt760By440WithoutScrollingOrTinyTypography()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var geometry = Read("src", "PowerFlow.App", "Dashboard", "ShellTransitionGeometry.cs");
        Assert.Contains("(760, 440)", geometry, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
        foreach (var size in new[] { "8", "9", "10" })
            Assert.DoesNotContain($"FontSize=\"{size}\"", xaml, StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}