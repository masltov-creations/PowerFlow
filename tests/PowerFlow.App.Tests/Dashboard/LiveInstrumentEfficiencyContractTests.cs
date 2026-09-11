using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class LiveInstrumentEfficiencyContractTests
{
    [Fact]
    public void Timeline_UsesThreeVisualRowsAndOverlaysCpuPerformanceOnPower()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");

        Assert.Contains("POWER / CPU PERF", xaml);
        Assert.Contains("StrokeDashArray=\"3,2\"", xaml);
        Assert.Contains("LaneWeights = [1.4d, 1.1d, 1.5d]", code);
        Assert.Contains("2 => 1, // CPU performance overlays package power.", code);
        Assert.Contains("UpdateTracePath(2, _data.Lanes[2], powerGeometry.Top", code);
    }

    [Fact]
    public void Settings_ExposeGranularVisibleAndBackgroundSampling()
    {
        var xaml = Read("src", "PowerFlow.App", "Settings", "SettingsPage.xaml");
        var app = Read("src", "PowerFlow.App", "App.xaml.cs");
        Assert.Contains("VisibleTelemetryIntervalBox", xaml);
        Assert.Contains("BackgroundTelemetryIntervalBox", xaml);
        Assert.Contains("Minimum=\"250\" Maximum=\"5000\"", xaml);
        Assert.Contains("Minimum=\"500\" Maximum=\"10000\"", xaml);
        Assert.Contains("_telemetryRecorder?.UpdateCadence", app);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}