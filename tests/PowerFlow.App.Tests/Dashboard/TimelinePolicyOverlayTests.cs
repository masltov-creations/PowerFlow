using PowerFlow.App.Dashboard;
using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class TimelinePolicyOverlayTests
{
    private static readonly OperatingEnvelope Learned = new(20, 50, 80, 28, 48, 76);
    private static readonly PerformanceEntitlement LearnedEntitlement = new(EnvelopeZone.Boost, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(10), true);

    [Fact]
    public void Build_MapsPressureAndPowerRailsToTheirNativeTimelineLanes()
    {
        var timeline = Timeline();
        var tuning = new EnvelopeTuning(EnvelopeTuningLayer.Tuned, ecoCeilingPressure: 25, efficientCeilingPressure: 55, responsiveCeilingPressure: 85);

        var overlay = TimelinePolicyOverlayProjection.Build(timeline, Learned, LearnedEntitlement, tuning);

        var efficient = Assert.Single(overlay.ValueRails, r => r.Kind == TimelinePolicyHandleKind.EfficientPressure);
        Assert.Equal(PerformanceTimelineMetric.CpuPressure, efficient.Metric);
        Assert.Equal(50, efficient.LearnedValue);
        Assert.Equal(55, efficient.CandidateValue);
        Assert.Equal(.50, efficient.LearnedNormalizedY, 3);
        Assert.Equal(.45, efficient.CandidateNormalizedY, 3);
        Assert.True(efficient.Editable);

        var power = Assert.Single(overlay.ValueRails, r => r.Kind == TimelinePolicyHandleKind.EfficientPowerFrontier);
        Assert.Equal(PerformanceTimelineMetric.PackagePower, power.Metric);
        Assert.Equal(48, power.LearnedValue);
        Assert.Equal(48, power.CandidateValue);
        Assert.False(power.Editable);
        Assert.InRange(power.CandidateNormalizedY, 0, 1);
    }

    [Fact]
    public void Build_MapsQualificationLeaseAndReleaseToSharedTimeAxisEndingAtNow()
    {
        var timeline = Timeline();
        var tuning = new EnvelopeTuning(EnvelopeTuningLayer.Tuned,
            qualificationDuration: TimeSpan.FromSeconds(6),
            leaseDuration: TimeSpan.FromSeconds(18),
            releaseHysteresis: TimeSpan.FromSeconds(8));

        var overlay = TimelinePolicyOverlayProjection.Build(timeline, Learned, LearnedEntitlement, tuning);

        var qualify = Assert.Single(overlay.TimeBands, b => b.Kind == TimelinePolicyHandleKind.QualificationDuration);
        Assert.Equal(1d, qualify.CandidateEndX);
        Assert.Equal(.90, qualify.CandidateStartX, 3);
        Assert.Equal(6, qualify.CandidateDuration.TotalSeconds);
        Assert.Equal(4, qualify.LearnedDuration.TotalSeconds);

        var lease = Assert.Single(overlay.TimeBands, b => b.Kind == TimelinePolicyHandleKind.LeaseDuration);
        Assert.Equal(.70, lease.CandidateStartX, 3);
        var release = Assert.Single(overlay.TimeBands, b => b.Kind == TimelinePolicyHandleKind.ReleaseHysteresis);
        Assert.Equal(.867, release.CandidateStartX, 3);
    }

    [Fact]
    public void DraggingPressureHandleChangesOnlyCandidateBoundary()
    {
        var original = EnvelopeTuning.Learned;

        var changed = TimelinePolicyInteraction.ApplyDrag(
            original, TimelinePolicyHandleKind.EfficientPressure,
            normalizedX: .5, normalizedCanvasY: .10,
            Learned, LearnedEntitlement, windowSeconds: 60);

        Assert.Equal(60, changed.EfficientCeilingPressure);
        Assert.Null(changed.EcoCeilingPressure);
        Assert.Null(changed.ResponsiveCeilingPressure);
        Assert.Null(original.EfficientCeilingPressure);
    }

    [Fact]
    public void DraggingTimeEdgeChangesOnlyCandidateDuration()
    {
        var original = EnvelopeTuning.Learned;

        var changed = TimelinePolicyInteraction.ApplyDrag(
            original, TimelinePolicyHandleKind.QualificationDuration,
            normalizedX: .90, normalizedCanvasY: .5,
            Learned, LearnedEntitlement, windowSeconds: 60);

        Assert.Equal(6, changed.QualificationDuration!.Value.TotalSeconds, 3);
        Assert.Null(changed.LeaseDuration);
        Assert.Null(changed.ReleaseHysteresis);
        Assert.Null(original.QualificationDuration);
    }

    private static PerformanceTimelineData Timeline()
    {
        var t0 = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var observations = new[]
        {
            new OperatingObservation(t0, 20, 25, 1800, 3, 16, EnvelopeZone.Eco, null, EnvelopeDecisionKind.None),
            new OperatingObservation(t0.AddSeconds(60), 70, 95, 4200, 12, 16, EnvelopeZone.Responsive, "build.exe", EnvelopeDecisionKind.Lease)
        };
        return PerformanceTimelineProjection.Build(observations, 60);
    }
}