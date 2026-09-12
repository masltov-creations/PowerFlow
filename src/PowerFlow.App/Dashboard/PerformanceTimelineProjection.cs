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
    double DomainMin,
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

        var powerSeries = SuppressIsolatedSpikes(visible.Select(x => x.observation.PackageWatts).ToArray(), minimumExcursion: 150d, neighborAgreement: 40d);
        var powerValues = powerSeries.Where(value => value is double).Select(value => value!.Value).ToArray();
        var powerRange = FocusRange(powerValues, minimumSpan: 40d, quantum: 5d, hardFloor: 0d, fallbackMin: 0d, fallbackMax: 100d);

        var performanceSeries = SuppressIsolatedSpikes(visible.Select(x => x.observation.ProcessorPerformancePercent).ToArray(), minimumExcursion: 90d, neighborAgreement: 25d);
        var performanceValues = performanceSeries.Where(value => value is double).Select(value => value!.Value).ToArray();
        var performanceRange = FocusRange(performanceValues, minimumSpan: 30d, quantum: 5d, hardFloor: 0d, fallbackMin: 75d, fallbackMax: 150d, includeValue: 100d);

        var coreMax = Math.Max(1, visible.Select(x => x.observation.TotalCores ?? x.observation.ActiveCores ?? 0).DefaultIfEmpty(1).Max());

        double X(DateTimeOffset at) => Math.Clamp((at - start).TotalSeconds / seconds, 0, 1);

        var lanes = new[]
        {
            Lane(PerformanceTimelineMetric.CpuPressure, "COMPUTE PRESSURE", "%", 0, 100, visible, X, x => x.CpuPressurePercent),
            Lane(PerformanceTimelineMetric.PackagePower, "PACKAGE POWER", "W", powerRange.Min, powerRange.Max, visible, X, x => x.PackageWatts, powerSeries),
            Lane(PerformanceTimelineMetric.EffectiveClock, "CPU PERFORMANCE", "%", performanceRange.Min, performanceRange.Max, visible, X, x => x.ProcessorPerformancePercent, performanceSeries),
            Lane(PerformanceTimelineMetric.ActiveCores, "CORES AWAKE", "cores", 0, coreMax, visible, X, x => x.ActiveCores)
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
        double domainMin,
        double domainMax,
        IReadOnlyList<(OperatingObservation observation, int index)> visible,
        Func<DateTimeOffset, double> xSelector,
        Func<OperatingObservation, double?> valueSelector,
        IReadOnlyList<double?>? valuesOverride = null)
    {
        var min = double.IsFinite(domainMin) ? domainMin : 0d;
        var max = double.IsFinite(domainMax) ? domainMax : min + 1d;
        if (max <= min) max = min + 1d;
        var span = max - min;
        var points = visible.Select((item, sequenceIndex) =>
        {
            var value = valuesOverride is not null ? valuesOverride[sequenceIndex] : valueSelector(item.observation);
            var valid = value is double raw && double.IsFinite(raw) && raw >= 0;
            return new TimelineSamplePoint(
                item.index,
                xSelector(item.observation.At),
                valid ? Math.Clamp((value!.Value - min) / span, 0, 1) : null,
                valid ? value : null);
        }).ToArray();
        return new TimelineLaneProjection(metric, label, unit, min, max, points);
    }

    private static double?[] SuppressIsolatedSpikes(IReadOnlyList<double?> values, double minimumExcursion, double neighborAgreement)
    {
        var result = values
            .Select(value => value is double raw && double.IsFinite(raw) && raw >= 0 ? (double?)raw : null)
            .ToArray();
        if (result.Length < 3) return result;

        for (var i = 1; i < result.Length - 1; i++)
        {
            if (result[i - 1] is not double previous || result[i] is not double current || result[i + 1] is not double next) continue;
            if (Math.Abs(previous - next) > neighborAgreement) continue;
            var neighborCenter = (previous + next) / 2d;
            if (Math.Abs(current - neighborCenter) < minimumExcursion) continue;
            result[i] = null;
        }

        return result;
    }

    private static (double Min, double Max) FocusRange(
        IReadOnlyList<double> values,
        double minimumSpan,
        double quantum,
        double hardFloor,
        double fallbackMin,
        double fallbackMax,
        double? includeValue = null)
    {
        var valid = values.Where(value => double.IsFinite(value) && value >= hardFloor).ToList();
        if (includeValue is double included && double.IsFinite(included)) valid.Add(Math.Max(hardFloor, included));
        if (valid.Count == 0) return (fallbackMin, fallbackMax);

        var low = valid.Min();
        var high = valid.Max();
        var span = high - low;
        if (span < minimumSpan)
        {
            var center = (low + high) / 2d;
            low = center - minimumSpan / 2d;
            high = center + minimumSpan / 2d;
        }

        low = Math.Max(hardFloor, low - quantum);
        high += quantum;
        low = Math.Max(hardFloor, Math.Floor(low / quantum) * quantum);
        high = Math.Ceiling(high / quantum) * quantum;
        if (high - low < minimumSpan)
            high = Math.Ceiling((low + minimumSpan) / quantum) * quantum;
        return (low, Math.Max(low + quantum, high));
    }

    private static PerformanceTimelineData Empty(DateTimeOffset start, DateTimeOffset latest, double seconds, PerformanceTimelineMode mode)
    {
        var lanes = new[]
        {
            new TimelineLaneProjection(PerformanceTimelineMetric.CpuPressure, "COMPUTE PRESSURE", "%", 0, 100, Array.Empty<TimelineSamplePoint>()),
            new TimelineLaneProjection(PerformanceTimelineMetric.PackagePower, "PACKAGE POWER", "W", 0, 100, Array.Empty<TimelineSamplePoint>()),
            new TimelineLaneProjection(PerformanceTimelineMetric.EffectiveClock, "CPU PERFORMANCE", "%", 75, 150, Array.Empty<TimelineSamplePoint>()),
            new TimelineLaneProjection(PerformanceTimelineMetric.ActiveCores, "CORES AWAKE", "cores", 0, 1, Array.Empty<TimelineSamplePoint>())
        };
        return new PerformanceTimelineData(start, latest, seconds, mode, lanes, Array.Empty<TimelineEventMarker>());
    }
}
