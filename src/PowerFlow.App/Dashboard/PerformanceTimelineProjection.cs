using PowerFlow.Core.Envelope;

namespace PowerFlow.App.Dashboard;

public enum PerformanceTimelineMetric
{
    CpuPressure,
    PackagePower,
    EffectiveClock,
    ActiveCores
}

public enum PerformanceTimelineMode
{
    Stacked,
    NormalizedOverlay
}

public sealed record TimelineSamplePoint(int ObservationIndex, double X, double? Y, double? Value);

public sealed record TimelineLaneProjection(
    PerformanceTimelineMetric Metric,
    string Label,
    string Unit,
    double DomainMax,
    IReadOnlyList<TimelineSamplePoint> Points);

public sealed record TimelineEventMarker(
    int ObservationIndex,
    double X,
    EnvelopeDecisionKind Decision,
    string? Actor,
    EnvelopeZone Zone);

public sealed record PerformanceTimelineData(
    DateTimeOffset WindowStart,
    DateTimeOffset Latest,
    double WindowSeconds,
    PerformanceTimelineMode Mode,
    IReadOnlyList<TimelineLaneProjection> Lanes,
    IReadOnlyList<TimelineEventMarker> Events);

public static class PerformanceTimelineProjection
{
    public static PerformanceTimelineData Build(
        IReadOnlyList<OperatingObservation> observations,
        double windowSeconds = 60,
        PerformanceTimelineMode mode = PerformanceTimelineMode.Stacked)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var seconds = Math.Max(1, windowSeconds);
        if (observations.Count == 0)
        {
            var now = DateTimeOffset.UtcNow;
            return Empty(now.AddSeconds(-seconds), now, seconds, mode);
        }

        var latest = observations.Max(x => x.At);
        var start = latest.AddSeconds(-seconds);
        var visible = observations
            .Select((observation, index) => (observation, index))
            .Where(x => x.observation.At >= start && x.observation.At <= latest)
            .OrderBy(x => x.observation.At)
            .ToArray();

        var powerMax = NiceCeiling(visible.Where(x => x.observation.PackageWatts.HasValue).Select(x => x.observation.PackageWatts!.Value), 25, 25);
        var clockMax = NiceCeiling(visible.Where(x => x.observation.EffectiveClockMhz.HasValue).Select(x => x.observation.EffectiveClockMhz!.Value), 500, 1000);
        var coreMax = Math.Max(1, visible.Select(x => x.observation.TotalCores ?? x.observation.ActiveCores ?? 0).DefaultIfEmpty(1).Max());

        double X(DateTimeOffset at) => Math.Clamp((at - start).TotalSeconds / seconds, 0, 1);

        var lanes = new[]
        {
            Lane(PerformanceTimelineMetric.CpuPressure, "CPU PRESSURE", "%", 100, visible, X, x => x.CpuPressurePercent),
            Lane(PerformanceTimelineMetric.PackagePower, "PACKAGE POWER", "W", powerMax, visible, X, x => x.PackageWatts),
            Lane(PerformanceTimelineMetric.EffectiveClock, "EFFECTIVE CLOCK", "MHz", clockMax, visible, X, x => x.EffectiveClockMhz),
            Lane(PerformanceTimelineMetric.ActiveCores, "ACTIVE CORES", "cores", coreMax, visible, X, x => x.ActiveCores)
        };

        var events = new List<TimelineEventMarker>();
        EnvelopeZone? previousZone = null;
        foreach (var item in visible)
        {
            var observation = item.observation;
            var zoneChanged = previousZone.HasValue && previousZone.Value != observation.Zone;
            if (observation.Decision != EnvelopeDecisionKind.None || !string.IsNullOrWhiteSpace(observation.Actor) || zoneChanged)
                events.Add(new TimelineEventMarker(item.index, X(observation.At), observation.Decision, observation.Actor, observation.Zone));
            previousZone = observation.Zone;
        }

        return new PerformanceTimelineData(start, latest, seconds, mode, lanes, events);
    }

    public static int? FindNearestObservationIndex(PerformanceTimelineData data, double normalizedX)
    {
        ArgumentNullException.ThrowIfNull(data);
        var points = data.Lanes.FirstOrDefault()?.Points;
        if (points is null || points.Count == 0) return null;
        var x = Math.Clamp(normalizedX, 0, 1);
        return points.OrderBy(point => Math.Abs(point.X - x)).ThenBy(point => point.X).First().ObservationIndex;
    }

    private static TimelineLaneProjection Lane(
        PerformanceTimelineMetric metric,
        string label,
        string unit,
        double domainMax,
        IReadOnlyList<(OperatingObservation observation, int index)> visible,
        Func<DateTimeOffset, double> xSelector,
        Func<OperatingObservation, double?> valueSelector)
    {
        var max = Math.Max(double.Epsilon, domainMax);
        var points = visible.Select(item =>
        {
            var value = valueSelector(item.observation);
            var valid = value is double raw && double.IsFinite(raw) && raw >= 0;
            return new TimelineSamplePoint(
                item.index,
                xSelector(item.observation.At),
                valid ? Math.Clamp(value!.Value / max, 0, 1) : null,
                valid ? value : null);
        }).ToArray();
        return new TimelineLaneProjection(metric, label, unit, domainMax, points);
    }

    private static double NiceCeiling(IEnumerable<double> values, double step, double minimum)
    {
        var valid = values.Where(x => double.IsFinite(x) && x > 0).ToArray();
        if (valid.Length == 0) return minimum;
        return Math.Max(minimum, Math.Ceiling(valid.Max() / step) * step);
    }

    private static PerformanceTimelineData Empty(DateTimeOffset start, DateTimeOffset latest, double seconds, PerformanceTimelineMode mode)
    {
        var lanes = new[]
        {
            new TimelineLaneProjection(PerformanceTimelineMetric.CpuPressure, "CPU PRESSURE", "%", 100, Array.Empty<TimelineSamplePoint>()),
            new TimelineLaneProjection(PerformanceTimelineMetric.PackagePower, "PACKAGE POWER", "W", 25, Array.Empty<TimelineSamplePoint>()),
            new TimelineLaneProjection(PerformanceTimelineMetric.EffectiveClock, "EFFECTIVE CLOCK", "MHz", 1000, Array.Empty<TimelineSamplePoint>()),
            new TimelineLaneProjection(PerformanceTimelineMetric.ActiveCores, "ACTIVE CORES", "cores", 1, Array.Empty<TimelineSamplePoint>())
        };
        return new PerformanceTimelineData(start, latest, seconds, mode, lanes, Array.Empty<TimelineEventMarker>());
    }
}
