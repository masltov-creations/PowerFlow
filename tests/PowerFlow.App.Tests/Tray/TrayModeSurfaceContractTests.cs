using Xunit;

namespace PowerFlow.App.Tests.Tray;

public sealed class TrayModeSurfaceContractTests
{
    [Fact]
    public void Tray_ExposesAllPowerFlowModesAndRoutesThemThroughSharedProfilePath()
    {
        var root = FindRoot();
        var host = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "Tray", "TrayIconHost.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "PowerFlow.App", "App.xaml.cs"));
        foreach (var label in new[] { "Auto", "Saver", "Balanced", "Performance", "Ultra" })
            Assert.Contains($"\"{label}\"", host, StringComparison.Ordinal);
        foreach (var selection in new[] { "Auto", "Eco", "Efficient", "Boost", "Ultra" })
            Assert.Contains($"ApplyOperatingModeAsync(PowerModeSelection.{selection})", app, StringComparison.Ordinal);
        Assert.DoesNotContain("case TrayMenuCommands.PowerSaver: await _controller.SetManualStateAsync", app, StringComparison.Ordinal);
        Assert.Contains("_powerModeProfileRuntime.CurrentProfile?.Mode", app, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return dir;
    }
}