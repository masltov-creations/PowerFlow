using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PerformanceAtlasFirstUserContractTests
{
    [Fact]
    public void Atlas_ExplainsModelAndShowsCurrentPointInsteadOfMysteryHeatmap()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasControl.xaml.cs");

        foreach (var name in new[] { "WhatLearnedText", "WhatDoingNowText", "ConfidenceExplanationText", "CurrentPointLayer", "AtlasUnavailableMessage" })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("YOU ARE HERE", code, StringComparison.Ordinal);
        Assert.Contains("SetModelExplanation", code, StringComparison.Ordinal);
        Assert.Contains("UpdateDimensionAvailability", code, StringComparison.Ordinal);
        Assert.Contains("IsEnabled", code, StringComparison.Ordinal);
        Assert.Contains("No usable observations", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_CanReportDimensionAvailabilityBeforeRendering()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "PerformanceAtlasProjection.cs");
        Assert.Contains("AvailableDimensions", code, StringComparison.Ordinal);
        Assert.Contains("HasUsableValues", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_FeedsModelExplanationFromEffectiveModelAndCurrentObservation()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("ModelExplanationProjection.Create", code, StringComparison.Ordinal);
        Assert.Contains("PerformanceAtlas.SetModelExplanation", code, StringComparison.Ordinal);
        Assert.Contains("learningModel.Envelope", code, StringComparison.Ordinal);
        Assert.Contains("learningModel.Confidence", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}
