using PowerFlow.Core.Envelope;

namespace PowerFlow.App.Dashboard;

public enum TimelinePolicyHandleKind
{
    EcoPressure,
    EfficientPressure,
    ResponsivePressure,
    EcoPowerFrontier,
    EfficientPowerFrontier,
    ResponsivePowerFrontier,
    QualificationDuration,
    LeaseDuration,
    ReleaseHysteresis
}

public sealed record TimelinePolicyValueRail(
    TimelinePolicyHandleKind Kind,
    PerformanceTimelineMetric Metric,
    string Label,
    double LearnedValue,
    double CandidateValue,
    double LearnedNormalizedY,
    double CandidateNormalizedY,
    bool Editable);

public sealed record TimelinePolicyTimeBand(
    TimelinePolicyHandleKind Kind,
    string Label,
    TimeSpan LearnedDuration,
    TimeSpan CandidateDuration,
    double LearnedStartX,
    double LearnedEndX,
    double CandidateStartX,
    double CandidateEndX,
    bool Editable);

public sealed record TimelinePolicyOverlayData(
    IReadOnlyList<TimelinePolicyValueRail> ValueRails,
    IReadOnlyList<TimelinePolicyTimeBand> TimeBands);

public static class TimelinePolicyOverlayProjection
{
    public static TimelinePolicyOverlayData Build(
        PerformanceTimelineData timeline,
        OperatingEnvelope learnedEnvelope,
        PerformanceEntitlement learnedEntitlement,
        EnvelopeTuning candidateTuning)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(learnedEnvelope);
        ArgumentNullException.ThrowIfNull(learnedEntitlement);
        ArgumentNullException.ThrowIfNull(candidateTuning);

        var candidateEnvelope = candidateTuning.ApplyTo(learnedEnvelope);
        var candidateEntitlement = candidateTuning.ApplyTo(learnedEntitlement);
        var rails = new List<TimelinePolicyValueRail>
        {
            Pressure(TimelinePolicyHandleKind.EcoPressure, "ECO", learnedEnvelope.EcoCeilingPressure, candidateEnvelope.EcoCeilingPressure),
            Pressure(TimelinePolicyHandleKind.EfficientPressure, "EFFICIENT", learnedEnvelope.EfficientCeilingPressure, candidateEnvelope.EfficientCeilingPressure),
            Pressure(TimelinePolicyHandleKind.ResponsivePressure, "RESPONSIVE", learnedEnvelope.ResponsiveCeilingPressure, candidateEnvelope.ResponsiveCeilingPressure)
        };

        AddPowerRail(rails, timeline, TimelinePolicyHandleKind.EcoPowerFrontier, "ECO POWER", learnedEnvelope.EcoPowerFrontierWatts, candidateEnvelope.EcoPowerFrontierWatts);
        AddPowerRail(rails, timeline, TimelinePolicyHandleKind.EfficientPowerFrontier, "EFFICIENT POWER", learnedEnvelope.EfficientPowerFrontierWatts, candidateEnvelope.EfficientPowerFrontierWatts);
        AddPowerRail(rails, timeline, TimelinePolicyHandleKind.ResponsivePowerFrontier, "RESPONSIVE POWER", learnedEnvelope.ResponsivePowerFrontierWatts, candidateEnvelope.ResponsivePowerFrontierWatts);

        var seconds = Math.Max(1, timeline.WindowSeconds);
        var bands = new[]
        {
            TimeBand(TimelinePolicyHandleKind.QualificationDuration, "QUALIFY", learnedEntitlement.QualificationDuration, candidateEntitlement.QualificationDuration, seconds),
            TimeBand(TimelinePolicyHandleKind.LeaseDuration, "LEASE", learnedEntitlement.LeaseDuration, candidateEntitlement.LeaseDuration, seconds),
            TimeBand(TimelinePolicyHandleKind.ReleaseHysteresis, "RELEASE", learnedEntitlement.ReleaseHysteresis, candidateEntitlement.ReleaseHysteresis, seconds)
        };
        return new TimelinePolicyOverlayData(rails, bands);
    }

    private static TimelinePolicyValueRail Pressure(TimelinePolicyHandleKind kind, string label, double learned, double candidate) =>
        new(kind, PerformanceTimelineMetric.CpuPressure, label, learned, candidate,
            NormalizeY(learned, 100), NormalizeY(candidate, 100), true);

    private static void AddPowerRail(List<TimelinePolicyValueRail> rails, PerformanceTimelineData timeline, TimelinePolicyHandleKind kind, string label, double? learned, double? candidate)
    {
        if (learned is not double learnedWatts || candidate is not double candidateWatts) return;
        var lane = timeline.Lanes.FirstOrDefault(lane => lane.Metric == PerformanceTimelineMetric.PackagePower);
        var domainMin = lane?.DomainMin ?? 0d;
        var domainMax = lane?.DomainMax ?? Math.Max(learnedWatts, candidateWatts);
        rails.Add(new TimelinePolicyValueRail(kind, PerformanceTimelineMetric.PackagePower, label,
            learnedWatts, candidateWatts,
            NormalizeY(learnedWatts, domainMin, domainMax),
            NormalizeY(candidateWatts, domainMin, domainMax), false));
    }

    private static TimelinePolicyTimeBand TimeBand(TimelinePolicyHandleKind kind, string label, TimeSpan learned, TimeSpan candidate, double windowSeconds) =>
        new(kind, label, learned, candidate,
            NormalizeStart(learned, windowSeconds), 1d,
            NormalizeStart(candidate, windowSeconds), 1d,
            true);

    private static double NormalizeY(double value, double domainMax) => NormalizeY(value, 0d, domainMax);
    private static double NormalizeY(double value, double domainMin, double domainMax)
    {
        var span = Math.Max(double.Epsilon, domainMax - domainMin);
        return 1d - Math.Clamp((value - domainMin) / span, 0d, 1d);
    }
    private static double NormalizeStart(TimeSpan duration, double windowSeconds) => 1d - Math.Clamp(duration.TotalSeconds / Math.Max(1d, windowSeconds), 0d, 1d);
}