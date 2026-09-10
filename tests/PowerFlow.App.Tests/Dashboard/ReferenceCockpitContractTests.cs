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
            "PowerFlowCanvasBrush", "PowerFlowSurfaceBrush", "PowerFlowSurfaceElevatedBrush",
            "PowerFlowAccentBrush", "PowerFlowSaverAccentBrush", "PowerFlowBalancedAccentBrush",
            "PowerFlowPerformanceAccentBrush", "PowerFlowAutoAccentBrush", "PowerFlowTextSecondaryBrush"
        })
            Assert.Contains($"x:Key=\"{key}\"", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_HasReferenceCockpitScaffoldingUsingRealPowerFlowData()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var modes = Read("src", "PowerFlow.App", "Dashboard", "PowerModeControl.xaml");
        var stats = Read("src", "PowerFlow.App", "Dashboard", "LiveStatsControl.xaml");
        var context = Read("src", "PowerFlow.App", "Dashboard", "ControlContextControl.xaml");

        foreach (var name in new[]
        {
            "CockpitRoot", "NavigationRail", "SystemHeaderHost", "PowerModeBandHost",
            "PrimaryAnalyticalRow", "LiveStatsHost", "ControlContextBandHost",
            "SecondaryOperationalRow", "StatusFooter"
        })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("{Binding IsPowerSaverSelected", modes, StringComparison.Ordinal);
        Assert.Contains("{Binding IsBalancedSelected", modes, StringComparison.Ordinal);
        Assert.Contains("{Binding IsPerformanceSelected", modes, StringComparison.Ordinal);
        Assert.Contains("{Binding IsAutoSelected", modes, StringComparison.Ordinal);
        Assert.Contains("LIVE STATS", stats, StringComparison.Ordinal);
        Assert.Contains("CONTROL / LOCK", context, StringComparison.Ordinal);
        Assert.DoesNotContain("GPU", xaml + stats, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CPU Temp", xaml + stats, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dashboard_UsesDensityAwareModeControlAndNoTinyExplicitFonts()
    {
        var sources = new[]
        {
            Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml"),
            Read("src", "PowerFlow.App", "Dashboard", "PowerModeControl.xaml"),
            Read("src", "PowerFlow.App", "Dashboard", "LiveStatsControl.xaml"),
            Read("src", "PowerFlow.App", "Dashboard", "ControlContextControl.xaml"),
            Read("src", "PowerFlow.App", "Dashboard", "OperationalContextControl.xaml")
        };
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var modes = sources[1];

        Assert.Contains("CurrentChipLayer", modes, StringComparison.Ordinal);
        Assert.Contains("SegmentedLayer", modes, StringComparison.Ordinal);
        Assert.Contains("CardsLayer", modes, StringComparison.Ordinal);
        Assert.Contains("OnPowerModeRequested", code, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateModeSelector", code, StringComparison.Ordinal);
        foreach (var source in sources)
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
        Assert.Contains("PowerModeBandHost.Presentation = profile.Modes", code, StringComparison.Ordinal);
        Assert.Contains("SystemHeaderHost.Presentation = profile.Header", code, StringComparison.Ordinal);
        Assert.Contains("LiveStatsHost.Presentation = profile.Stats", code, StringComparison.Ordinal);
        Assert.Contains("ControlContextBandHost.Presentation = profile.ControlContext", code, StringComparison.Ordinal);
        Assert.DoesNotContain("shell.ShowModeCards", code, StringComparison.Ordinal);
        Assert.DoesNotContain("shell.ShowLiveStatsPanel", code, StringComparison.Ordinal);
        Assert.DoesNotContain("shell.ShowLowerContextPanels", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}