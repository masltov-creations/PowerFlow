using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class AdaptiveTuningContractTests
{
    [Fact]
    public void TimelineAndAtlas_ExposePersistentExactIndexSelectionAndMainWindowSharesIt()
    {
        var timelineXaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml");
        var timelineCode = Read("src", "PowerFlow.App", "Dashboard", "PerformanceTimelineControl.xaml.cs");
        var atlasCode = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml.cs");
        var main = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("x:Name=\"SelectionLayer\"", timelineXaml, StringComparison.Ordinal);
        Assert.Contains("PointerPressed=\"OnPointerPressed\"", timelineXaml, StringComparison.Ordinal);
        Assert.Contains("TimelineSelectionChangedEventArgs", timelineCode, StringComparison.Ordinal);
        Assert.Contains("event EventHandler<TimelineSelectionChangedEventArgs>? SelectionChanged", timelineCode, StringComparison.Ordinal);
        Assert.Contains("SetSelectedObservationIndices", timelineCode, StringComparison.Ordinal);
        Assert.Contains("SetSelectedObservationIndices", atlasCode, StringComparison.Ordinal);
        Assert.Contains("PerformanceTimeline.SelectionChanged += OnTimelineSelectionChanged", main, StringComparison.Ordinal);
        Assert.Contains("PerformanceAtlas.SelectionChanged += OnAtlasSelectionChanged", main, StringComparison.Ordinal);
        Assert.Contains("UpdateAnalyticalSelection", main, StringComparison.Ordinal);
        Assert.Contains("PerformanceTimeline.SetSelectedObservationIndices", main, StringComparison.Ordinal);
        Assert.Contains("PerformanceAtlas.SetSelectedObservationIndices", main, StringComparison.Ordinal);
    }

    [Fact]
    public void TuneSurface_UsesSemanticRailsWithAccessibleNonDragAlternatives()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        foreach (var name in new[]
        {
            "TuneControlRegion", "TuningLayerBadge", "EcoBoundarySlider", "EcoBoundaryNumber",
            "EfficientBoundarySlider", "EfficientBoundaryNumber", "ResponsiveBoundarySlider", "ResponsiveBoundaryNumber",
            "LeaseDurationSlider", "LeaseDurationNumber", "MaximumZoneSelector", "ReplaySummaryText", "ResetLearnedButton"
        })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);

        foreach (var accessibleName in new[] { "Eco boundary", "Efficient boundary", "Responsive boundary", "Boost lease duration", "Maximum semantic zone" })
            Assert.Contains($"AutomationProperties.Name=\"{accessibleName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Slider", xaml, StringComparison.Ordinal);
        Assert.Contains("<NumberBox", xaml, StringComparison.Ordinal);
        Assert.Contains("LEARNED", xaml, StringComparison.Ordinal);
        Assert.Contains("COUNTERFACTUAL", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TunePresentation_IsContextualAndContainsNoRawActuatorControls()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("ApplyTuningPresentation", code, StringComparison.Ordinal);
        Assert.Contains("string.Equals(_currentSection, \"tune\"", code, StringComparison.Ordinal);
        Assert.Contains("EnvelopeTuningViewModel", code, StringComparison.Ordinal);
        foreach (var raw in new[] { "powercfg", "EPP", "P-state" })
            Assert.DoesNotContain(raw, xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TuneSurface_DoesNotUseTinyExplicitTypography()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        foreach (var size in new[] { "8", "9", "10" })
            Assert.DoesNotContain($"FontSize=\"{size}\"", xaml, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}