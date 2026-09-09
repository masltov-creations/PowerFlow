using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class SecondaryAnchorComponentTests
{
    [Fact]
    public void LiveStatsControl_ContainsThreeSemanticPresentations()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "LiveStatsControl.xaml.cs");
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "LiveStatsControl.xaml");
        Assert.Contains("StatsPresentation Presentation", code, StringComparison.Ordinal);
        Assert.Contains("InlineStats", xaml, StringComparison.Ordinal);
        Assert.Contains("CompactStatsRail", xaml, StringComparison.Ordinal);
        Assert.Contains("FullStatsRail", xaml, StringComparison.Ordinal);
        Assert.Contains("MEMORY", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlContextControl_PreservesCauseRailAndDistinctExpandedModules()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "ControlContextControl.xaml.cs");
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "ControlContextControl.xaml");
        Assert.Contains("ControlContextPresentation Presentation", code, StringComparison.Ordinal);
        Assert.Contains("CauseLineLayer", xaml, StringComparison.Ordinal);
        Assert.Contains("ContextRailLayer", xaml, StringComparison.Ordinal);
        Assert.Contains("ContextModulesLayer", xaml, StringComparison.Ordinal);
        Assert.Contains("ControlLockModule", xaml, StringComparison.Ordinal);
        Assert.Contains("ActiveCauseModule", xaml, StringComparison.Ordinal);
        Assert.Contains("PowerFlowNextModule", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationalContextControl_UsesTruthfulOperationalRegions()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "OperationalContextControl.xaml.cs");
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "OperationalContextControl.xaml");
        Assert.Contains("SecondaryPresentation Presentation", code, StringComparison.Ordinal);
        Assert.Contains("RulesRequested", code, StringComparison.Ordinal);
        Assert.Contains("SettingsRequested", code, StringComparison.Ordinal);
        Assert.Contains("RecentEventsRegion", xaml, StringComparison.Ordinal);
        Assert.Contains("ActiveSourcesRegion", xaml, StringComparison.Ordinal);
        Assert.Contains("QuickActionsRegion", xaml, StringComparison.Ordinal);
        Assert.Contains("ACTIVE SOURCES", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TOP POWER CONSUMERS", xaml, StringComparison.OrdinalIgnoreCase);
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