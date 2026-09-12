using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class TensionShadowWiringTests
{
    [Fact]
    public void App_EvaluatesShadowBesideCurrentAutoButNeverActuatesIt()
    {
        var app = Read("src", "PowerFlow.App", "App.xaml.cs");

        Assert.Contains("private readonly TensionShadowRuntime _tensionShadowRuntime = new();", app, StringComparison.Ordinal);
        Assert.Contains("var shadowEvaluation = _tensionShadowRuntime.Evaluate", app, StringComparison.Ordinal);
        Assert.Contains("_latestTensionShadowEvaluation = shadowEvaluation", app, StringComparison.Ordinal);
        Assert.Contains("_config.EffectiveGovernorTensionPercent", app, StringComparison.Ordinal);
        Assert.Contains("() => _latestTensionShadowEvaluation", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyAdaptiveGovernorEvaluationAsync(shadowEvaluation)", app, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_AcceptsReadOnlyShadowProvider()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("Func<TensionShadowEvaluation?>? tensionShadowProvider", code, StringComparison.Ordinal);
        Assert.Contains("_tensionShadowProvider = tensionShadowProvider", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        var path = Path.Combine(new[] { dir }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Expected source file {path}");
        return File.ReadAllText(path);
    }
}
