using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ExpandedCockpitLayoutTests
{
    [Fact]
    public void Expanded_HasReferenceCockpitRegionsInOperationalOrder()
    {
        var xaml = ReadMainWindow();
        foreach (var name in new[]
        {
            "CockpitRoot",
            "SystemHeaderHost",
            "PowerModeBandHost",
            "PrimaryAnalyticalRow",
            "ControlContextBandHost",
            "SecondaryOperationalRow",
            "StatusFooter"
        })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("<ColumnDefinition Width=\"7*\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"3*\"", xaml, StringComparison.Ordinal);

        var header = xaml.IndexOf("x:Name=\"SystemHeaderHost\"", StringComparison.Ordinal);
        var modes = xaml.IndexOf("x:Name=\"PowerModeBandHost\"", StringComparison.Ordinal);
        var primary = xaml.IndexOf("x:Name=\"PrimaryAnalyticalRow\"", StringComparison.Ordinal);
        var context = xaml.IndexOf("x:Name=\"ControlContextBandHost\"", StringComparison.Ordinal);
        var secondary = xaml.IndexOf("x:Name=\"SecondaryOperationalRow\"", StringComparison.Ordinal);
        var footer = xaml.IndexOf("x:Name=\"StatusFooter\"", StringComparison.Ordinal);
        Assert.True(header < modes && modes < primary && primary < context && context < secondary && secondary < footer);
    }

    [Fact]
    public void Expanded_UsesOneInstanceOfEachMorphingAnchor()
    {
        var xaml = ReadMainWindow();
        Assert.Equal(1, Count(xaml, "<dash:ShellHeaderControl"));
        Assert.Equal(1, Count(xaml, "<dash:PowerModeControl"));
        Assert.Equal(1, Count(xaml, "<dash:LiveStatsControl"));
        Assert.Equal(1, Count(xaml, "<dash:TrajectoryControl"));
        Assert.Equal(1, Count(xaml, "<dash:ControlContextControl"));
        Assert.Equal(1, Count(xaml, "<dash:OperationalContextControl"));
    }

    [Fact]
    public void Expanded_DoesNotRetainOldGenericCardWallScaffolding()
    {
        var xaml = ReadMainWindow();
        foreach (var obsolete in new[]
        {
            "BrandHeader",
            "ModeSelectorHost",
            "HeroTrajectoryPanel",
            "LiveStatsPanel",
            "LowerContextGrid",
            "ActiveRulePanel",
            "RecentEventsPanel",
            "QuickActionsPanel"
        })
            Assert.DoesNotContain($"x:Name=\"{obsolete}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_DispatchesSemanticPresentationsToSharedAnchors()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("SystemHeaderHost.Presentation = profile.Header", code, StringComparison.Ordinal);
        Assert.Contains("PowerModeBandHost.Presentation = profile.Modes", code, StringComparison.Ordinal);
        Assert.Contains("LiveStatsHost.Presentation = profile.Stats", code, StringComparison.Ordinal);
        Assert.Contains("ControlContextBandHost.Presentation = profile.ControlContext", code, StringComparison.Ordinal);
        Assert.Contains("SecondaryOperationalRow.Presentation = profile.Secondary", code, StringComparison.Ordinal);
        Assert.Contains("Trajectory.SetShellPresentation(profile.Trajectory", code, StringComparison.Ordinal);
        Assert.DoesNotContain("LiveStatsPanel.Visibility", code, StringComparison.Ordinal);
        Assert.DoesNotContain("LowerContextGrid.Visibility", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CockpitActions_ReuseExistingNavigationAndManualReleasePaths()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var contextCode = Read("src", "PowerFlow.App", "Dashboard", "ControlContextControl.xaml.cs");
        Assert.Contains("OnOperationalRulesRequested", code, StringComparison.Ordinal);
        Assert.Contains("OnOperationalSettingsRequested", code, StringComparison.Ordinal);
        Assert.Contains("OnReleaseManualRequested", code, StringComparison.Ordinal);
        Assert.Contains("ReleaseManualRequested", contextCode, StringComparison.Ordinal);
        Assert.Contains("ReleaseManualLatchAsync", code, StringComparison.Ordinal);
    }

    private static string ReadMainWindow() => Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");

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
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException("PowerFlow repo root not found");
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}