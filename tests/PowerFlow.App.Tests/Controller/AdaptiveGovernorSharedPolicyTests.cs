using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class AdaptiveGovernorSharedPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 10, 5, 0, 0, TimeSpan.Zero);
    private static readonly OperatingEnvelope Frozen = new(12, 32, 58, 24, 42, 68);
    private static readonly EnvelopeTuning SavedOverride = new(
        EnvelopeTuningLayer.Override,
        ecoCeilingPressure: 10,
        efficientCeilingPressure: 28,
        responsiveCeilingPressure: 52,
        manualOverrideZone: EnvelopeZone.Efficient);

    [Fact]
    public void BackgroundRuntime_UsesPersistedFrozenModelAndSavedTuning()
    {
        var config = Config();
        var runtime = new AdaptiveGovernorRuntime();
        var snapshot = new ControllerSnapshot(
            PowerState.PowerSaver, "sample", false, null, 95, 0, null, null,
            T0.AddSeconds(40), Array.Empty<TransitionRecord>(), 1, 0);

        var result = runtime.Evaluate(snapshot, History(), config);

        Assert.NotNull(result);
        Assert.Equal(Frozen, result!.LearnedEnvelope);
        Assert.Equal(EnvelopeConfidence.High, result.Confidence);
        Assert.Equal(10, result.EffectiveEnvelope.EcoCeilingPressure);
        Assert.Equal(28, result.EffectiveEnvelope.EfficientCeilingPressure);
        Assert.Equal(52, result.EffectiveEnvelope.ResponsiveCeilingPressure);
        Assert.Equal(EnvelopeZone.Efficient, result.Decision.AllowedZone);
        Assert.Contains("override", result.Decision.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DashboardModelProjection_UsesSamePersistedFrozenModelAndSavedTuning()
    {
        var config = Config();
        var vm = new DashboardViewModel();
        vm.Configure(config);
        var history = History();
        var snapshot = new ControllerSnapshot(
            PowerState.PowerSaver, "sample", false, null, 95, 0, null, null,
            history[^1].At, Array.Empty<TransitionRecord>(), 1, 0);

        vm.UpdateContinuity(snapshot, history, new DashboardTelemetry(80, 4400, history[^1].At, null, "TEST-HOST", null, 32));

        Assert.NotEmpty(vm.OperatingHistory);
        Assert.All(vm.OperatingHistory, observation => Assert.Equal(EnvelopeZone.Efficient, observation.Zone));
        Assert.Contains("override", vm.GovernorModelExplanation, StringComparison.OrdinalIgnoreCase);
    }

    private static PowerFlowConfig Config() => PowerFlowConfig.Default with
    {
        AdaptiveGovernor = new AdaptiveGovernorSettings(SavedOverride, true, Frozen, EnvelopeConfidence.High)
    };

    private static IReadOnlyList<ContinuitySample> History() => Enumerable.Range(0, 10)
        .Select(i => new ContinuitySample(
            T0.AddSeconds(i * 4),
            80 + i,
            55 + i * 2,
            3200 + i * 90,
            PowerState.Balanced,
            "sample",
            false,
            null,
            0,
            null,
            ActiveCores: null,
            TotalCores: 32))
        .ToArray();
}
