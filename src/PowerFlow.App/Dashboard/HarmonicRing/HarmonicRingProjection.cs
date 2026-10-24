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

public sealed record RingReading(
    double? Value,
    DateTimeOffset? ObservedAt,
    RingReadingQuality Quality,
    string Unit = "",
    TimeSpan? Age = null)
{
    public static RingReading Missing { get; } = new(null, null, RingReadingQuality.Missing);
    public bool IsAvailable => Quality != RingReadingQuality.Missing && Value is double;
    public bool IsCurrent => Quality == RingReadingQuality.Current && IsAvailable;
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
    EnvelopeZone? ModelZone,
    string? ConfirmedAppliedProfile)
{
    public string? ConfirmedAppliedAuthority { get; init; }
    /// <summary>Stable timestamp stamp for this projected snapshot.</summary>
    public long Sequence => Now.UtcDateTime.Ticks;
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
    public const int MaximumAllowedPoints = 128;
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DefaultFreshness = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan DefaultMaximumGap = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan TrendWindow = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan TrendMinimumSpan = TimeSpan.FromSeconds(3);
    public const int TrendMinimumSamples = 3;
    public const double TrendDeadbandPercent = 5d;

    public static HarmonicRingSnapshot Build(
        IReadOnlyList<OperatingObservation> history,
        DateTimeOffset now,
        string? confirmedAppliedProfile = null,
        TimeSpan? window = null,
        TimeSpan? freshness = null,
        int maximumPoints = DefaultMaximumPoints,
        string? confirmedAppliedAuthority = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (maximumPoints is < 4 or > MaximumAllowedPoints)
            throw new ArgumentOutOfRangeException(nameof(maximumPoints));

        var visibleWindow = window ?? DefaultWindow;
        var freshWithin = freshness ?? DefaultFreshness;
        if (visibleWindow <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        if (freshWithin < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(freshness));

        // Exclude future observations. A timestamp can appear more than once after merging
        // retained streams, so use a value-based tie-break instead of input order.
        var ordered = history
            .Where(item => item.At <= now)
            .OrderBy(item => item.At)
            .ThenBy(item => item.At.Offset.Ticks)
            .ThenBy(item => item.CpuPressurePercent)
            .ThenBy(item => item.PackageWatts)
            .ThenBy(item => item.EffectiveClockMhz)
            .ThenBy(item => item.ActiveCores)
            .ThenBy(item => item.TotalCores)
            .ThenBy(item => item.ProcessorPerformancePercent)
            .ThenBy(item => item.Zone)
            .ThenBy(item => item.Actor, StringComparer.Ordinal)
            .ThenBy(item => item.Decision)
            .GroupBy(item => item.At)
            .Select(group => group.Last())
            .ToArray();

        var latest = ordered.LastOrDefault();
        RingReading From(OperatingObservation? sample, Func<OperatingObservation, double?> select, string unit)
        {
            if (sample is null) return new RingReading(null, null, RingReadingQuality.Missing, unit);
            var value = select(sample);
            if (value is not double finite || !double.IsFinite(finite) || finite < 0)
                return new RingReading(null, null, RingReadingQuality.Missing, unit);
            var age = now - sample.At;
            return new RingReading(value, sample.At,
                age <= freshWithin ? RingReadingQuality.Current : RingReadingQuality.Stale,
                unit,
                age);
        }

        RingReading MostRecent(Func<OperatingObservation, double?> select, string unit)
            => From(ordered.LastOrDefault(sample =>
                select(sample) is double value && double.IsFinite(value) && value >= 0), select, unit);

        var pressure = From(latest, sample => sample.CpuPressurePercent, "pressure %");
        var watts = MostRecent(sample => sample.PackageWatts, "W");
        var clock = MostRecent(sample => sample.EffectiveClockMhz, "MHz");
        var awake = MostRecent(sample => sample.ActiveCores, "cores");
        var total = MostRecent(sample => sample.TotalCores, "cores");

        var start = now - visibleWindow;
        var withinWindow = ordered.Where(item => item.At >= start).ToArray();
        var wave = BuildWave(withinWindow, start, visibleWindow, maximumPoints);
        var trend = pressure.IsCurrent ? ComputeTrend(ordered, now) : RingPressureTrend.Unknown;

        return new HarmonicRingSnapshot(
            now,
            latest?.At,
            visibleWindow,
            pressure,
            trend,
            new RingReading(null, null, RingReadingQuality.Missing, "CPU busy %"),
            watts,
            clock,
            awake,
            total,
            wave,
            latest?.Zone,
            string.IsNullOrWhiteSpace(confirmedAppliedProfile) ? null : confirmedAppliedProfile)
        {
            ConfirmedAppliedAuthority = string.IsNullOrWhiteSpace(confirmedAppliedAuthority)
                ? null
                : confirmedAppliedAuthority
        };
    }

    private static RingPressureTrend ComputeTrend(
        IReadOnlyList<OperatingObservation> ordered,
        DateTimeOffset now)
    {
        var recent = ordered.Where(item => item.At >= now - TrendWindow).ToArray();
        if (recent.Length == 0) return RingPressureTrend.Unknown;

        var segmentStart = recent.Length - 1;
        while (segmentStart > 0 && recent[segmentStart].At - recent[segmentStart - 1].At <= DefaultMaximumGap)
            segmentStart--;
        var continuous = recent[segmentStart..];
        if (continuous.Length < TrendMinimumSamples) return RingPressureTrend.Unknown;
        if (continuous[^1].At - continuous[0].At < TrendMinimumSpan)
            return RingPressureTrend.Unknown;

        // Averaging each endpoint pair limits one-sample noise while preserving sustained change.
        var early = (continuous[0].CpuPressurePercent + continuous[1].CpuPressurePercent) / 2d;
        var late = (continuous[^2].CpuPressurePercent + continuous[^1].CpuPressurePercent) / 2d;
        var delta = late - early;
        if (delta >= TrendDeadbandPercent) return RingPressureTrend.Building;
        if (delta <= -TrendDeadbandPercent) return RingPressureTrend.Ebbing;
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
        return Array.AsReadOnly(points.ToArray());
    }
}
