using PowerFlow.App.Dashboard;
using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ProductSurfaceRegressionTests
{
    [Fact]
    public void SectionNavigation_DoesNotResizeShellAndBrandingUsesPackagedAssets()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("ms-appx:///Assets/PowerFlow.png", xaml, StringComparison.Ordinal);
        Assert.Contains("AppWindow.SetIcon(iconPath)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("tag != \"flow\" && _shellState", code, StringComparison.Ordinal);
        Assert.Contains("Content=\"Tune Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Changes how the new Auto model reacts to demand", xaml, StringComparison.Ordinal);
        Assert.Contains("Manual Saver, Balanced, Performance, and Ultra profiles are not changed", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelSurface_IsFixedPowerPerformanceExplanationWithoutVisibleAxisPickers()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml");
        Assert.Contains("POWER / PERFORMANCE MODEL", xaml, StringComparison.Ordinal);
        Assert.Contains("package power left to right, CPU performance bottom to top", xaml, StringComparison.Ordinal);
        Assert.Contains("<StackPanel Visibility=\"Collapsed\">", xaml, StringComparison.Ordinal);
        Assert.Contains("MODEL CONFIDENCE", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelCurrentPoint_UsesProcessorPerformancePercent()
    {
        var source = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml.cs");
        var model = Read("src", "PowerFlow.App", "Dashboard", "ModelExplanationProjection.cs");
        Assert.Contains("ProcessorPerformancePercent", model, StringComparison.Ordinal);
        Assert.Contains("latest.ProcessorPerformancePercent", model, StringComparison.Ordinal);
        Assert.Contains("PerformanceAtlasDimension.EffectiveClock => point.ProcessorPerformancePercent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PerformanceAtlasDimension.EffectiveClock => point.EffectiveClockMhz", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultPowerPerformanceAxes_DoNotJumpAtNormalDesktopValues()
    {
        var t = DateTimeOffset.UnixEpoch;
        var observations = new[]
        {
            new OperatingObservation(t, 25, 82, 4200, 5, 16, EnvelopeZone.Efficient, null, EnvelopeDecisionKind.None, 124),
            new OperatingObservation(t.AddSeconds(1), 31, 108, 4350, 6, 16, EnvelopeZone.Efficient, null, EnvelopeDecisionKind.None, 129)
        };
        var data = PerformanceAtlasProjection.Build(observations, PerformanceAtlasDimension.PackagePower, PerformanceAtlasDimension.EffectiveClock);
        Assert.Equal(150, data.XAxis.DomainMax);
        Assert.Equal(150, data.YAxis.DomainMax);
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(path).ToArray()));
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PowerFlow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate PowerFlow repo root.");
    }
}