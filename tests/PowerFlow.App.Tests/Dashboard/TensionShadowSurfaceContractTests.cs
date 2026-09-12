using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class TensionShadowSurfaceContractTests
{
    [Fact]
    public void LiveSurface_ShowsCurrentAndShadowModelsWithContinuousTensionControl()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");

        Assert.Contains("GOVERNOR MODELS", xaml, StringComparison.Ordinal);
        foreach (var name in new[]
        {
            "CurrentAutoStateText", "CurrentAutoDetailText", "TensionShadowStateText", "TensionShadowDetailText",
            "GovernorTensionSlider", "GovernorTensionValueText", "GovernorReferenceText", "GovernorDetailPanel"
        }) Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
        Assert.Matches("x:Name=\\\"GovernorTensionSlider\\\"[^>]*Minimum=\\\"0\\\"[^>]*Maximum=\\\"100\\\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Governor tension shadow model\"", xaml, StringComparison.Ordinal);
        foreach (var label in new[] { "RELAXED", "RESPONSIVE", "SAVER", "BAL-E", "BAL-P", "PERF" })
            Assert.Contains($"Text=\"{label}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveSurface_WiresShadowProjectionAndGhostEnvelope()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        Assert.Contains("GovernorComparisonProjection.Create", code, StringComparison.Ordinal);
        Assert.Contains("PerformanceTimeline.SetShadowPolicyContext", code, StringComparison.Ordinal);
        Assert.Contains("_tensionShadowProvider?.Invoke()", code, StringComparison.Ordinal);
        Assert.Contains("GovernorTensionPolicy.Resolve", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TensionSlider_UpdatesOnlyShadowConfigurationNeverOperatingMode()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("GovernorTensionSlider.ValueChanged += OnGovernorTensionChanged", code, StringComparison.Ordinal);
        var body = MethodBody(code, "private async void OnGovernorTensionChanged");
        Assert.Contains("GovernorTensionPercent", body, StringComparison.Ordinal);
        Assert.Contains("ApplyConfigFromPageAsync", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_applyOperatingMode", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerModeProfileRuntime", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SetManualStateAsync", body, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactPresentation_KeepsGovernorModelsVisibleWhileRichDetailIsProgressive()
    {
        var layout = Read("src", "PowerFlow.App", "Dashboard", "PowerFlowShellLayout.cs");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("GovernorControlPresentation.Bias", layout, StringComparison.Ordinal);
        Assert.Contains("new ShellGeometry(0, 10, 8, 42, 82)", layout, StringComparison.Ordinal);
        Assert.Contains("GovernorDetailPanel.Visibility", code, StringComparison.Ordinal);
        Assert.Contains("GovernorControlPresentation.Contextual or GovernorControlPresentation.Deep", code, StringComparison.Ordinal);
        Assert.Contains("GovernorDetailPanel.Opacity", code, StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method {signature}");
        var open = source.IndexOf('{', start);
        Assert.True(open >= 0);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        throw new Xunit.Sdk.XunitException($"Unclosed method {signature}");
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}
