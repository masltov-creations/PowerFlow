using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PerformanceAtlasContractTests
{
    [Fact]
    public void Atlas_IsOneInteractiveHeatmapWithDimensionFrontierAndSelectionLayers()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml.cs");

        foreach (var name in new[]
        {
            "AtlasRoot", "XDimensionSelector", "YDimensionSelector", "AtlasCanvas",
            "FrontierLayer", "SelectionLayer", "InteractionLayer", "XAxisLabel", "YAxisLabel"
        })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("PERFORMANCE ATLAS", xaml, StringComparison.Ordinal);
        Assert.Contains("PerformanceAtlasProjection.Build", code, StringComparison.Ordinal);
        Assert.Contains("AtlasSelectionChangedEventArgs", code, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged", code, StringComparison.Ordinal);
        Assert.Contains("SetSelectedObservationIndices", code, StringComparison.Ordinal);
        Assert.Contains("OperatingEnvelope", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new Window", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Atlas_DimensionSelectorsExposeOnlyTruthfulInitialMetrics()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml");
        foreach (var label in new[] { "Package Power", "Effective Clock", "Active Cores", "CPU Pressure" })
            Assert.Contains(label, xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GPU", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Temperature", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shell_UsesOneAnalyticalHostForTimelineAndAtlas()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("x:Name=\"AnalyticalInstrumentHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TimelineInstrumentColumn\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AtlasInstrumentColumn\"", xaml, StringComparison.Ordinal);
        Assert.Equal(1, Count(xaml, "<dash:PerformanceTimelineControl"));
        Assert.Equal(1, Count(xaml, "<dash:PerformanceAtlasControl"));
        Assert.Contains("ApplyAnalyticalInstrumentLayout", code, StringComparison.Ordinal);
        Assert.Contains("PerformanceAtlas.Apply(ViewModel.OperatingHistory, learningModel.Envelope", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandedModelSwitchesCentralInstrumentWhileFullScreenPairsBoth()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("string.Equals(_currentSection, \"model\"", code, StringComparison.Ordinal);
        Assert.Contains("PowerFlowShellState.FullScreen", code, StringComparison.Ordinal);
        Assert.Contains("TimelineInstrumentColumn.Width", code, StringComparison.Ordinal);
        Assert.Contains("AtlasInstrumentColumn.Width", code, StringComparison.Ordinal);
        Assert.Contains("PerformanceTimeline.Visibility", code, StringComparison.Ordinal);
        Assert.Contains("PerformanceAtlas.Visibility", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Atlas_DoesNotUseTinyExplicitTypography()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml");
        foreach (var size in new[] { "8", "9", "10" })
            Assert.DoesNotContain($"FontSize=\"{size}\"", xaml, StringComparison.Ordinal);
    }

    private static int Count(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
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
