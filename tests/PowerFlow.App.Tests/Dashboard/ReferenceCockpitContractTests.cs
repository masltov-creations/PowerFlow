using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ReferenceCockpitContractTests
{
    [Fact]
    public void Theme_DefinesNavyGlassAndSemanticStateAccents()
    {
        var app = Read("src", "PowerFlow.App", "App.xaml");
        foreach (var key in new[]
        {
            "PowerFlowCanvasBrush",
            "PowerFlowSurfaceBrush",
            "PowerFlowSurfaceElevatedBrush",
            "PowerFlowAccentBrush",
            "PowerFlowSaverAccentBrush",
            "PowerFlowBalancedAccentBrush",
            "PowerFlowPerformanceAccentBrush",
            "PowerFlowAutoAccentBrush",
            "PowerFlowTextSecondaryBrush"
        })
            Assert.Contains($"x:Key=\"{key}\"", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_HasReferenceCockpitHierarchyUsingRealPowerFlowData()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");

        foreach (var name in new[]
        {
            "BrandHeader",
            "NavigationRail",
            "ModeSelectorHost",
            "SaverModeButton",
            "BalancedModeButton",
            "PerformanceModeButton",
            "AutoModeButton",
            "HeroTrajectoryPanel",
            "LiveStatsPanel",
            "LowerContextGrid",
            "ActiveRulePanel",
            "RecentEventsPanel",
            "QuickActionsPanel"
        })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);

        Assert.Equal(1, Count(xaml, "<dash:TrajectoryControl x:Name=\"Trajectory\""));
        Assert.Contains("{Binding CpuLabel}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding WattsLabel}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding FrequencyLabel}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding TriggerApplication}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding PromotionRuleLabel}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding QuietRuleLabel}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GPU", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RAM", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CPU Temp", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dashboard_UsesPersistentModeCardsAndNoTinyExplicitFonts()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("OnSaverModeClicked", xaml, StringComparison.Ordinal);
        Assert.Contains("OnBalancedModeClicked", xaml, StringComparison.Ordinal);
        Assert.Contains("OnPerformanceModeClicked", xaml, StringComparison.Ordinal);
        Assert.Contains("OnAutoModeClicked", xaml, StringComparison.Ordinal);
        Assert.Contains("UpdateModeSelector", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FontSize=\"10\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FontSize=\"9\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FontSize=\"8\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellLayout_DrivesCockpitDisclosureInsteadOfHardCodingExpandedOnly()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("shell.ShowModeCards", code, StringComparison.Ordinal);
        Assert.Contains("shell.ShowLiveStatsPanel", code, StringComparison.Ordinal);
        Assert.Contains("shell.ShowLowerContextPanels", code, StringComparison.Ordinal);
        Assert.Contains("ModeSelectorHost.Visibility", code, StringComparison.Ordinal);
        Assert.Contains("LiveStatsPanel.Visibility", code, StringComparison.Ordinal);
        Assert.Contains("LowerContextGrid.Visibility", code, StringComparison.Ordinal);
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