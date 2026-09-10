using PowerFlow.App.Dashboard;
using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class EnvelopeTuningViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 9, 20, 0, 0, TimeSpan.Zero);
    private static readonly OperatingEnvelope LearnedEnvelope = new(20, 50, 80, 25, 45, 75);
    private static readonly PerformanceEntitlement LearnedEntitlement = new(
        EnvelopeZone.Boost,
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(12),
        TimeSpan.FromSeconds(6),
        true);

    [Fact]
    public void AnalyticalSelection_PreservesExactIndicesTimeRangeAndSharedActor()
    {
        var history = History();
        var timeline = AnalyticalSelection.FromObservationIndices(history, new[] { 2 });
        Assert.Equal(new[] { 2 }, timeline.ObservationIndices);
        Assert.Equal(T0.AddSeconds(2), timeline.Start);
        Assert.Equal(T0.AddSeconds(2), timeline.End);
        Assert.Equal("render.exe", timeline.Actor);

        var atlas = AnalyticalSelection.FromObservationIndices(history, new[] { 1, 2, 2 });
        Assert.Equal(new[] { 1, 2 }, atlas.ObservationIndices);
        Assert.Equal(T0.AddSeconds(1), atlas.Start);
        Assert.Equal(T0.AddSeconds(2), atlas.End);
        Assert.Equal("render.exe", atlas.Actor);

        var backToTimeline = AnalyticalSelection.FromObservationIndices(history, atlas.ObservationIndices);
        Assert.Equal(atlas.ObservationIndices, backToTimeline.ObservationIndices);
        Assert.Equal(atlas.Start, backToTimeline.Start);
        Assert.Equal(atlas.End, backToTimeline.End);
        Assert.Equal(atlas.Actor, backToTimeline.Actor);
    }

    [Fact]
    public void AnalyticalSelection_MixedActorsDoesNotInventASingleActor()
    {
        var history = History();
        var selection = AnalyticalSelection.FromObservationIndices(history, new[] { 0, 2 });
        Assert.Null(selection.Actor);
        Assert.Equal(2, selection.ObservationIndices.Count);
    }

    [Fact]
    public void Load_StartsAtLearnedLayerWithContextualMachineAppLeaseAndBoundaryCopy()
    {
        var vm = LoadedVm();

        Assert.Equal(EnvelopeTuningLayer.Learned, vm.Layer);
        Assert.Equal("LEARNED", vm.LayerLabel);
        Assert.Contains("20", vm.BoundarySummary, StringComparison.Ordinal);
        Assert.Contains("50", vm.BoundarySummary, StringComparison.Ordinal);
        Assert.Contains("80", vm.BoundarySummary, StringComparison.Ordinal);
        Assert.Contains("render.exe", vm.ActorSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("12", vm.LeaseSummary, StringComparison.Ordinal);
        Assert.Contains("Boost", vm.EntitlementSummary, StringComparison.Ordinal);
        Assert.Null(vm.Replay.EstimatedEnergyDeltaWh);
        Assert.Null(vm.Replay.EstimatedPerformanceDeltaPercent);
    }

    [Fact]
    public void BoundaryAndLeaseEditsMoveToTunedAndRecomputeCounterfactual()
    {
        var vm = LoadedVm();

        vm.SetEfficientCeilingPressure(60);
        vm.SetLeaseDuration(TimeSpan.FromSeconds(8));
        vm.SetReleaseHysteresis(TimeSpan.FromSeconds(3));

        Assert.Equal(EnvelopeTuningLayer.Tuned, vm.Layer);
        Assert.Equal("TUNED", vm.LayerLabel);
        Assert.Equal(60, vm.CandidateTuning.EfficientCeilingPressure);
        Assert.Equal(TimeSpan.FromSeconds(8), vm.CandidateTuning.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), vm.CandidateTuning.ReleaseHysteresis);
        Assert.True(vm.Replay.ChangedDecisionCount >= 1);
        Assert.Contains("changed", vm.ReplaySummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppEntitlementEditIsSemanticNotRawActuatorConfiguration()
    {
        var vm = LoadedVm();
        vm.SetMaximumZone(EnvelopeZone.Responsive);
        vm.SetQualificationDuration(TimeSpan.FromSeconds(2));

        Assert.Equal(EnvelopeZone.Responsive, vm.CandidateTuning.MaximumZone);
        Assert.Equal(TimeSpan.FromSeconds(2), vm.CandidateTuning.QualificationDuration);
        Assert.Equal("TUNED", vm.LayerLabel);
        Assert.DoesNotContain("EPP", vm.EntitlementSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("P-state", vm.EntitlementSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powercfg", vm.EntitlementSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitSemanticOverrideUsesOverrideLayerAndCanResetToLearned()
    {
        var vm = LoadedVm();
        vm.SetManualOverride(EnvelopeZone.Efficient);

        Assert.Equal(EnvelopeTuningLayer.Override, vm.Layer);
        Assert.Equal("OVERRIDE", vm.LayerLabel);
        Assert.Equal(EnvelopeZone.Efficient, vm.CandidateTuning.ManualOverrideZone);
        Assert.Contains("explicit", vm.LayerExplanation, StringComparison.OrdinalIgnoreCase);

        vm.ResetToLearned();
        Assert.Equal(EnvelopeTuningLayer.Learned, vm.Layer);
        Assert.Equal("LEARNED", vm.LayerLabel);
        Assert.Null(vm.CandidateTuning.ManualOverrideZone);
        Assert.Equal(0, vm.Replay.ChangedDecisionCount);
    }

    [Fact]
    public void LearnedModelRefresh_PreservesUserTuningAndSelection()
    {
        var history = History();
        var vm = LoadedVm();
        vm.SetEfficientCeilingPressure(60);
        var selection = AnalyticalSelection.FromObservationIndices(history, new[] { 2 });
        var refreshed = new OperatingEnvelope(22, 52, 82, 26, 46, 76);

        vm.UpdateLearnedContext(refreshed, LearnedEntitlement, history, selection);

        Assert.Equal(EnvelopeTuningLayer.Tuned, vm.Layer);
        Assert.Equal(60, vm.CandidateTuning.EfficientCeilingPressure);
        Assert.Equal(new[] { 2 }, vm.Selection.ObservationIndices);
        Assert.Contains("60", vm.BoundarySummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyCandidateTuning_ReplacesUnsavedCandidateAndRecomputesReplay()
    {
        var vm = LoadedVm();
        var candidate = new EnvelopeTuning(
            EnvelopeTuningLayer.Tuned,
            efficientCeilingPressure: 62,
            qualificationDuration: TimeSpan.FromSeconds(2),
            leaseDuration: TimeSpan.FromSeconds(9),
            releaseHysteresis: TimeSpan.FromSeconds(4));

        vm.ApplyCandidateTuning(candidate);

        Assert.Equal(62, vm.CandidateTuning.EfficientCeilingPressure);
        Assert.Equal(TimeSpan.FromSeconds(2), vm.CandidateTuning.QualificationDuration);
        Assert.Equal(TimeSpan.FromSeconds(9), vm.CandidateTuning.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(4), vm.CandidateTuning.ReleaseHysteresis);
        Assert.Equal(EnvelopeTuningLayer.Tuned, vm.Layer);
        Assert.Equal(62, vm.CurrentEnvelope.EfficientCeilingPressure);
        Assert.True(vm.Replay.ObservationCount > 0);
    }    private static EnvelopeTuningViewModel LoadedVm()
    {
        var history = History();
        var selection = AnalyticalSelection.FromObservationIndices(history, new[] { 1, 2 });
        var vm = new EnvelopeTuningViewModel();
        vm.Load(LearnedEnvelope, LearnedEntitlement, history, selection);
        return vm;
    }

    private static IReadOnlyList<OperatingObservation> History() => new[]
    {
        new OperatingObservation(T0, 15, 25, 2200, 2, 8, EnvelopeZone.Eco, "browser.exe", EnvelopeDecisionKind.None),
        new OperatingObservation(T0.AddSeconds(1), 45, 42, 3300, 4, 8, EnvelopeZone.Efficient, "render.exe", EnvelopeDecisionKind.None),
        new OperatingObservation(T0.AddSeconds(2), 55, 55, 3900, 6, 8, EnvelopeZone.Responsive, "render.exe", EnvelopeDecisionKind.None),
        new OperatingObservation(T0.AddSeconds(3), 75, 70, 4300, 7, 8, EnvelopeZone.Responsive, "render.exe", EnvelopeDecisionKind.None),
    };
}