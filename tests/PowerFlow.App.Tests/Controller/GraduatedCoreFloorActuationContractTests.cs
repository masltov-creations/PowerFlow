using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class GraduatedCoreFloorActuationContractTests
{
    [Fact]
    public void Runtime_IsBackgroundOwnedAndMutuallyExclusiveWithLegacyAdaptiveActuation()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "src", "PowerFlow.App", "App.xaml.cs"));
        var config = File.ReadAllText(Path.Combine(RepoRoot(), "src", "PowerFlow.Core", "Rules", "PowerFlowConfig.cs"));
        var runtime = File.ReadAllText(Path.Combine(RepoRoot(), "src", "PowerFlow.App", "Controller", "GraduatedCoreFloorActuatorRuntime.cs"));
        Assert.Contains("GraduatedCoreActuationEnabled", config);
        Assert.Contains("_telemetryRecorder.ContinuityChanged += OnTelemetryContinuityChanged", app);
        Assert.Contains("coarseAdaptiveActuationEnabled: _config.AdaptiveActuationEnabled", app);
        Assert.Contains("previewMode: _previewMode", app);
        Assert.Contains("MaximumQualifiedCoreFloorPercent = 75", runtime);
        Assert.Contains("MinimumWriteInterval = TimeSpan.FromSeconds(5)", runtime);
        Assert.Contains("StopAndRestore", app);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PowerFlow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate PowerFlow repo root.");
    }}