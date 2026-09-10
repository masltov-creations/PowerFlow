using PowerFlow.App.Dashboard;
using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class AdaptiveGovernorPersistencePresentationTests
{
    [Fact]
    public void PersistedTuning_RestoresCandidateAndSurvivesLearnedRefresh()
    {
        var learned = new OperatingEnvelope(20, 50, 80, 25, 45, 75);
        var refreshed = new OperatingEnvelope(22, 54, 84, 27, 48, 79);
        var entitlement = PerformanceEntitlement.LegacyPerformance;
        var vm = new EnvelopeTuningViewModel();
        vm.Load(learned, entitlement, Array.Empty<OperatingObservation>(), AnalyticalSelection.Empty);
        var persisted = new EnvelopeTuning(
            EnvelopeTuningLayer.Override,
            ecoCeilingPressure: 16,
            efficientCeilingPressure: 44,
            responsiveCeilingPressure: 72,
            maximumZone: EnvelopeZone.Responsive,
            qualificationDuration: TimeSpan.FromSeconds(3),
            leaseDuration: TimeSpan.FromSeconds(14),
            releaseHysteresis: TimeSpan.FromSeconds(7),
            manualOverrideZone: EnvelopeZone.Efficient);

        vm.RestorePersistedTuning(persisted);
        vm.UpdateLearnedContext(refreshed, entitlement, Array.Empty<OperatingObservation>(), AnalyticalSelection.Empty);

        Assert.Equal(EnvelopeTuningLayer.Override, vm.Layer);
        Assert.Equal(16, vm.CandidateTuning.EcoCeilingPressure);
        Assert.Equal(44, vm.CandidateTuning.EfficientCeilingPressure);
        Assert.Equal(72, vm.CandidateTuning.ResponsiveCeilingPressure);
        Assert.Equal(EnvelopeZone.Responsive, vm.CandidateTuning.MaximumZone);
        Assert.Equal(EnvelopeZone.Efficient, vm.CandidateTuning.ManualOverrideZone);
        Assert.Equal(TimeSpan.FromSeconds(14), vm.CandidateTuning.LeaseDuration);
    }

    [Fact]
    public void TuneSurface_ExposesExplicitSavePauseAndPrecisionPolicyControls()
    {
        var xaml = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml");
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");

        foreach (var name in new[] { "SaveTuningButton", "PauseLearningToggle", "QualificationDurationNumber", "ReleaseHysteresisNumber" })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("Content=\"SAVE TUNING\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Pause learning\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OnSaveTuningClicked", code, StringComparison.Ordinal);
        Assert.Contains("OnPauseLearningToggled", code, StringComparison.Ordinal);
        Assert.Contains("EffectiveAdaptiveGovernorSettings", code, StringComparison.Ordinal);
        Assert.Contains("FrozenLearnedEnvelope", code, StringComparison.Ordinal);
        Assert.Contains("FrozenConfidence", code, StringComparison.Ordinal);
    }
    [Fact]
    public void TuneEditsStayCandidateUntilExplicitSaveHandler()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        var precisionHandler = Slice(code, "private void OnEcoBoundaryNumberChanged", "private void OnEfficientBoundaryNumberChanged");
        var graphHandler = Slice(code, "private void OnTimelinePolicyHandleChanged", "private void OnEcoBoundaryNumberChanged");
        Assert.DoesNotContain("_applyConfig", precisionHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("_applyConfig", graphHandler, StringComparison.Ordinal);
        Assert.Contains("ApplyCandidateTuning", graphHandler, StringComparison.Ordinal);
        var saveHandler = Slice(code, "OnSaveTuningClicked", "OnResetLearnedClicked");
        Assert.Contains("_applyConfig", saveHandler, StringComparison.Ordinal);
        Assert.Contains("AdaptiveGovernor", saveHandler, StringComparison.Ordinal);
    }
    [Fact]
    public void VisualModel_UsesSameEffectiveLearnedOrFrozenPolicyModel()
    {
        var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
        Assert.Contains("ResolveLearningModel(calibration)", code, StringComparison.Ordinal);
        Assert.Contains("learningModel.Envelope", code, StringComparison.Ordinal);
        Assert.Contains("learningModel.Confidence", code, StringComparison.Ordinal);
        Assert.Contains("learningModel.Envelope.EfficientPowerFrontierWatts", code, StringComparison.Ordinal);
        Assert.DoesNotContain("calibration.SustainedEfficiencyFrontierWatts", code, StringComparison.Ordinal);
    }
    private static string Slice(string text, string start, string end)
    {
        var a = text.IndexOf(start, StringComparison.Ordinal);
        if (a < 0) return string.Empty;
        var b = text.IndexOf(end, a + start.Length, StringComparison.Ordinal);
        return b < 0 ? text[a..] : text[a..b];
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "PowerFlow.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine(new[] { dir }.Concat(parts).ToArray()));
    }
}
