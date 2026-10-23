using PowerFlow.Core.Envelope;

namespace PowerFlow.App.Dashboard.HarmonicRing;

public enum RingReadingQuality
{
    Missing,
    Current,
    Stale
}

public enum RingPressureTrend
{
    Unknown,
    Building,
    Steady,
    Ebbing
}

public sealed record RingReading(double? Value, DateTimeOffset? ObservedAt, RingReadingQuality Quality)
{
    public static RingReading Missing { get; } = new(null, null, RingReadingQuality.Missing);
    public bool IsCurrent => Quality == RingReadingQuality.Current && Value is double;
}

public readonly record struct RingWavePoint(
    DateTimeOffset At,
    double FractionOfWindow,
    double PressurePercent,
    bool StartsSegment);

public sealed record HarmonicRingSnapshot(
    DateTimeOffset Now,
    DateTimeOffset? LatestObservationAt,
    TimeSpan Window,
    RingReading Pressure,
    RingPressureTrend PressureTrend,
    RingReading CpuUsage,
    RingReading PackageWatts,
    RingReading EffectiveClockMhz,
    RingReading AwakeCores,
    RingReading TotalCores,
    IReadOnlyList<RingWavePoint> PressureHistory,
    string? ConfirmedAppliedProfile)
{
    public bool HasCurrentPressure => Pressure.IsCurrent;
}

/// <summary>
/// Projects one already-retained observation sequence. No polling, actuation, sensor access or
/// animation clock is owned by this class. CpuUsage stays unavailable until an independent,
/// qualified utilization signal is added to the retained presentation snapshot.
/// </summary>
public static class HarmonicRingProjection
{
    public const int DefaultMaximumPoints = 96;
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DefaultFreshness = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan DefaultMaximumGap = TimeSpan.FromSeconds(5);

    public static HarmonicRingSnapshot Build(
        IReadOnlyList<OperatingObservation> history,
        DateTimeOffset now,
        string? confirmedAppliedProfile = null,
        TimeSpan? window = null,
        TimeSpan? freshness = null,
        int maximumPoints = DefaultMaximumPoints)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (maximumPoints < 4) throw new ArgumentOutOfRangeException(nameof(maximumPoints));

        var visibleWindow = window ?? DefaultWindow;
        var freshWithin = freshness ?? DefaultFreshness;
        if (visibleWindow <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        if (freshWithin < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(freshness));

        // Exclude future observations rather than presenting an invalid negative age as fresh.
        // Deduplicate equal timestamps deterministically while retaining the latest supplied item.
        var ordered = history
            .Where(item => item.At <= now)
            .Select((item, index) => (item, index))
            .OrderBy(entry => entry.item.At)
            .ThenBy(entry => entry.index)
            .GroupBy(entry => entry.item.At)
            .Select(group => group.Last().item)
            .ToArray();

        var latest = ordered.LastOrDefault();
        RingReading From(OperatingObservation? sample, Func<OperatingObservation, double?> select)
        {
            if (sample is null) return RingReading.Missing;
            var value = select(sample);
            if (value is not double finite || !double.IsFinite(finite) || finite < 0)
                return RingReading.Missing;
            var age = now - sample.At;
            return new RingReading(value, sample.At,
                age <= freshWithin ? RingReadingQuality.Current : RingReadingQuality.Stale);
        }

        OperatingObservation? MostRecent(Func<OperatingObservation, bool> present)
            => ordered.LastOrDefault(present);

        var pressure = From(latest, sample => sample.CpuPressurePercent);
        var watts = From(MostRecent(sample => sample.PackageWatts is double),
            sample => sample.PackageWatts);
        var clock = From(MostRecent(sample => sample.EffectiveClockMhz is double),
            sample => sample.EffectiveClockMhz);
        var awake = From(MostRecent(sample => sample.ActiveCores is int),
            sample => sample.ActiveCores);
        var total = From(MostRecent(sample => sample.TotalCores is int),
            sample => sample.TotalCores);

        var start = now - visibleWindow;
        var withinWindow = ordered.Where(item => item.At >= start).ToArray();
        var wave = BuildWave(withinWindow, start, visibleWindow, maximumPoints);
        var trend = pressure.IsCurrent ? ComputeTrend(withinWindow, now) : RingPressureTrend.Unknown;

        return new HarmonicRingSnapshot(
            now,
            latest?.At,
            visibleWindow,
            pressure,
            trend,
            RingReading.Missing, // pressure is not independent CPU usage
            watts,
            clock,
            awake,
            total,
            wave,
            string.IsNullOrWhiteSpace(confirmedAppliedProfile) ? null : confirmedAppliedProfile);
    }

    private static RingPressureTrend ComputeTrend(
        IReadOnlyList<OperatingObservation> visible,
        DateTimeOffset now)
    {
        var recent = visible.Where(item => item.At >= now.AddSeconds(-10)).ToArray();
        if (recent.Length < 3) return RingPressureTrend.Unknown;
        if ((recent[^1].At - recent[0].At).TotalSeconds < 3)
            return RingPressureTrend.Unknown;

        // Averaging each endpoint pair limits one-sample noise while preserving sustained change.
        var early = (recent[0].CpuPressurePercent + recent[1].CpuPressurePercent) / 2d;
        var late = (recent[^2].CpuPressurePercent + recent[^1].CpuPressurePercent) / 2d;
        var delta = late - early;
        if (delta >= 5d) return RingPressureTrend.Building;
        if (delta <= -5d) return RingPressureTrend.Ebbing;
        return RingPressureTrend.Steady;
    }

    private static IReadOnlyList<RingWavePoint> BuildWave(
        IReadOnlyList<OperatingObservation> samples,
        DateTimeOffset start,
        TimeSpan window,
        int maximumPoints)
    {
        if (samples.Count == 0) return Array.Empty<RingWavePoint>();

        var segment = 0;
        var annotated = new (OperatingObservation Observation, int Segment)[samples.Count];
        for (var i = 0; i < samples.Count; i++)
        {
            if (i > 0 && samples[i].At - samples[i - 1].At > DefaultMaximumGap)
                segment++;
            annotated[i] = (samples[i], segment);
        }

        var selected = new SortedSet<int>();
        selected.Add(0);
        selected.Add(samples.Count - 1);
        if (samples.Count <= maximumPoints)
        {
            for (var i = 0; i < samples.Count; i++) selected.Add(i);
        }
        else
        {
            // Two extrema per time bucket preserve pressure crests and troughs instead
            // of flattening them with fixed-stride subsampling.
            var interior = samples.Count - 2;
            var buckets = Math.Max(1, (maximumPoints - 2) / 2);
            for (var b = 0; b < buckets; b++)
            {
                var begin = 1 + (int)((long)b * interior / buckets);
                var end = 1 + (int)((long)(b + 1) * interior / buckets);
                if (begin >= end) continue;
                var min = begin;
                var max = begin;
                for (var i = begin + 1; i < end; i++)
                {
                    if (samples[i].CpuPressurePercent < samples[min].CpuPressurePercent) min = i;
                    if (samples[i].CpuPressurePercent > samples[max].CpuPressurePercent) max = i;
                }
                selected.Add(min);
                selected.Add(max);
            }
        }

        var points = new List<RingWavePoint>(selected.Count);
        int? previousSegment = null;
        foreach (var index in selected)
        {
            var item = annotated[index];
            var fraction = Math.Clamp((item.Observation.At - start).TotalSeconds /
                window.TotalSeconds, 0d, 1d);
            points.Add(new RingWavePoint(
                item.Observation.At, fraction, item.Observation.CpuPressurePercent,
                previousSegment is null || previousSegment != item.Segment));
            previousSegment = item.Segment;
        }
        return points;
    }
}
