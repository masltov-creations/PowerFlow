using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PowerModeStripContractTests
{
    [Fact]
    public void ModeStrip_ExposesFivePrimaryModesWithSelectionAuthorityAndTooltips()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PowerModeStripControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PowerModeStripControl.xaml.cs");

        foreach (var name in new[] { "AutoModeButton", "EcoModeButton", "EfficientModeButton", "ResponsiveModeButton", "BoostModeButton", "ModeAuthorityText" })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
        foreach (var label in new[] { "AUTO", "ECO", "EFFICIENT", "RESPONSIVE", "BOOST" })
            Assert.Contains($"Content=\"{label}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name", xaml, StringComparison.Ordinal);
        Assert.Contains("ModeRequested", code, StringComparison.Ordinal);
        Assert.Contains("SetSelection", code, StringComparison.Ordinal);
        Assert.Contains("IsChecked", code, StringComparison.Ordinal);
        Assert.Contains("MANUAL", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_EmbedsModeStripFromCompactUpwardAndForwardsRequests()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "ShellHeaderControl.xaml.cs");

        Assert.Contains("xmlns:dash=\"using:PowerFlow.App.Dashboard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompactModeStrip\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SystemModeStrip\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"MinimalModeStrip\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ModeRequested", code, StringComparison.Ordinal);
        Assert.Contains("SetModeSelection", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_MapsModeRequestsToExistingManualControllerPath()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("SystemHeaderHost.ModeRequested += OnModeRequested", code, StringComparison.Ordinal);
        Assert.Contains("SystemHeaderHost.SetModeSelection", code, StringComparison.Ordinal);
        Assert.Contains("ReleaseManualLatchAsync", code, StringComparison.Ordinal);
        Assert.Contains("SetManualStateAsync", code, StringComparison.Ordinal);
        Assert.Contains("PowerState.PowerSaver", code, StringComparison.Ordinal);
        Assert.Contains("PowerState.Balanced", code, StringComparison.Ordinal);
        Assert.Contains("PowerState.HighPerformance", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        var path = Path.Combine(new[] { dir }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Expected source file does not exist: {path}");
        return File.ReadAllText(path);
    }
}
