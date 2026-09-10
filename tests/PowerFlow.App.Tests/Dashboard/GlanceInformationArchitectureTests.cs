using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class GlanceInformationArchitectureTests
{
    [Fact]
    public void Glance_ContainsStateStatsTrajectoryCauseAndNextWithoutNavigation()
    {
        var profile = PowerFlowShellLayout.Resolve(320, 176, PowerFlowShellState.Glance, "flow");
        Assert.Equal(NavigationPresentation.None, profile.Navigation);
        Assert.Equal(HeaderPresentation.Minimal, profile.Header);
        Assert.Equal(ModePresentation.CurrentChip, profile.Modes);
        Assert.Equal(StatsPresentation.Inline, profile.Stats);
        Assert.Equal(TrajectoryPresentation.Minimal, profile.Trajectory);
        Assert.Equal(ControlContextPresentation.CauseLine, profile.ControlContext);
        Assert.Equal(SecondaryPresentation.Hidden, profile.Secondary);
    }

    [Fact]
    public void Glance_ReflowsSharedStatsAboveSharedTrajectoryInsteadOfKeepingExpandedColumns()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        Assert.Contains("ApplyGlancePrimaryLayout", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(LiveStatsInstrument, 0)", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(TrajectoryInstrument, 1)", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumnSpan(LiveStatsInstrument, 2)", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumnSpan(TrajectoryInstrument, 2)", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PrimaryStatsRow\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PrimaryGraphRow\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Glance_UsesSameSingleAnchorInstancesAsExpandedAndCompact()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        Assert.Equal(1, Count(xaml, "<dash:ShellHeaderControl"));
        Assert.Equal(1, Count(xaml, "<dash:PowerModeControl"));
        Assert.Equal(1, Count(xaml, "<dash:LiveStatsControl"));
        Assert.Equal(1, Count(xaml, "<dash:TrajectoryControl"));
        Assert.Equal(1, Count(xaml, "<dash:ControlContextControl"));
    }

    [Fact]
    public void Glance_PreservesTrayAnchorAndPinnedTapGrowthSemantics()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var geometry = Read("src", "PowerFlow.App", "Dashboard", "ShellTransitionGeometry.cs");
        Assert.Contains("_lastTrayAnchor", code, StringComparison.Ordinal);
        Assert.Contains("ShellTransitionGeometry.TargetBounds", code, StringComparison.Ordinal);
        Assert.Contains("PowerFlowShellState.Glance", geometry, StringComparison.Ordinal);
        Assert.Contains("(320, 176)", geometry, StringComparison.Ordinal);
        Assert.Contains("OnGlanceTapped", code, StringComparison.Ordinal);
        Assert.Contains("PowerFlowShellState.Compact", code, StringComparison.Ordinal);
        Assert.Contains("ShellActivationMode.TransientNoActivate", code, StringComparison.Ordinal);
        Assert.Contains("ShellActivationMode.PinnedActive", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Glance_MinimalHeaderUsesTinyBrandIdentityRatherThanRepeatingStateChip()
    {
        var header = Read("src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml");
        Assert.Contains("x:Name=\"GlanceBrandText\"", header, StringComparison.Ordinal);
        Assert.Contains("Text=\"PowerFlow\"", header, StringComparison.Ordinal);
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