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
    public void Dashboard_HasReferenceCockpitScaffoldingUsingRealPowerFlowData()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var modes = Read("src", "PowerFlow.App", "Dashboard", "PowerModeControl.xaml");

        foreach (var name in new[]
        {
            "BrandHeader",
            "NavigationRail",
            "ShellHeaderHost",
            "ModeSelectorHost",
            "PowerModeHost",
            "HeroTrajectoryPanel",
            "LiveStatsPanel",
            "LowerContextGrid",
            "ActiveRulePanel",
            "RecentEventsPanel",
            "QuickActionsPanel"
        })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);

        Assert.Equal(1, Count(xaml, "<dash:TrajectoryControl x:Name=\"Trajectory\""));
        Assert.Equal(1, Count(xaml, "<dash:PowerModeControl x:Name=\"PowerModeHost\""));
        Assert.Contains("{Binding IsPowerSaverSelected", modes, StringComparison.Ordinal);
        Assert.Contains("{Binding IsBalancedSelected", modes, StringComparison.Ordinal);
        Assert.Contains("{Binding IsPerformanceSelected", modes, StringComparison.Ordinal);
        Assert.Contains("{Binding IsAutoSelected", modes, StringComparison.Ordinal);
        Assert.DoesNotContain("GPU", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CPU Temp", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dashboard_UsesDensityAwareModeControlAndNoTinyExplicitFonts()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var modes = Read("src", "PowerFlow.App", "Dashboard", "PowerModeControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("CurrentChipLayer", modes, StringComparison.Ordinal);
        Assert.Contains("SegmentedLayer", modes, StringComparison.Ordinal);
        Assert.Contains("CardsLayer", modes, StringComparison.Ordinal);
        Assert.Contains("OnPowerModeRequested", code, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateModeSelector", code, StringComparison.Ordinal);
        foreach (var source in new[] { xaml, modes })
        {
            Assert.DoesNotContain("FontSize=\"10\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FontSize=\"9\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FontSize=\"8\"", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ShellLayout_DrivesSemanticPresentationInsteadOfInformationLossBooleans()
    {
        var layout = Read("src", "PowerFlow.App", "Dashboard", "PowerFlowShellLayout.cs");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("StatsPresentation.CompactRail", layout, StringComparison.Ordinal);
        Assert.Contains("ControlContextPresentation.Rail", layout, StringComparison.Ordinal);
        Assert.Contains("ModePresentation.Segmented", layout, StringComparison.Ordinal);
        Assert.Contains("PowerModeHost.Presentation = shell.Modes", code, StringComparison.Ordinal);
        Assert.Contains("ShellHeaderHost.Presentation = shell.Header", code, StringComparison.Ordinal);
        Assert.DoesNotContain("shell.ShowModeCards", code, StringComparison.Ordinal);
        Assert.DoesNotContain("shell.ShowLiveStatsPanel", code, StringComparison.Ordinal);
        Assert.DoesNotContain("shell.ShowLowerContextPanels", code, StringComparison.Ordinal);
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
