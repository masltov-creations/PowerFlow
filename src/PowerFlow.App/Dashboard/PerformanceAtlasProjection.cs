using PowerFlow.Core.Envelope;

namespace PowerFlow.App.Dashboard;

public enum PerformanceAtlasDimension
{
    PackagePower,
    EffectiveClock,
    ActiveCores,
    CpuPressure
}

public sealed record PerformanceAtlasAxis(
    PerformanceAtlasDimension Dimension,
    string Label,
    string Unit,
    double DomainMin,
    double DomainMax,
    int BinCount);

public sealed record PerformanceAtlasCell(
    int XBin,
    int YBin,
    double Density,
    int SampleCount,
    double? EfficiencyValue,
    IReadOnlyList<int> ObservationIndices);

public sealed record PerformanceAtlasDimensionPair(
    PerformanceAtlasDimension X,
    PerformanceAtlasDimension Y,
    bool Changed,
    string? Explanation,
    bool IsAvailable);

public sealed record PerformanceAtlasPointProjection(
    double X,
    double Y,
    double XValue,
    double YValue);
public sealed record PerformanceAtlasData(
    PerformanceAtlasAxis XAxis,
    PerformanceAtlasAxis YAxis,
    IReadOnlyList<PerformanceAtlasCell> Cells,
    IReadOnlyList<int> IncludedObservationIndices);

public static class PerformanceAtlasProjection
{
    public static IReadOnlyList<PerformanceAtlasDimension> AvailableDimensions(IReadOnlyList<OperatingObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        return Enum.GetValues<PerformanceAtlasDimension>()
            .Where(dimension => observations.Any(observation => IsValid(Value(observation, dimension))))
            .ToArray();
    }

    public static bool HasUsableValues(IReadOnlyList<OperatingObservation> observations, PerformanceAtlasDimension dimension)
    {
        ArgumentNullException.ThrowIfNull(observations);
        return observations.Any(observation => IsValid(Value(observation, dimension)));
    }

    public static bool HasUsablePair(IReadOnlyList<OperatingObservation> observations, PerformanceAtlasDimension x, PerformanceAtlasDimension y)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (x == y) return false;
        return observations.Any(observation => IsValid(Value(observation, x)) && IsValid(Value(observation, y)));
    }
    public static int UsableObservationCount(IReadOnlyList<OperatingObservation> observations, PerformanceAtlasDimension dimension)
    {
        ArgumentNullException.ThrowIfNull(observations);
        return observations.Count(observation => IsValid(Value(observation, dimension)));
    }

    public static PerformanceAtlasDimensionPair ResolveAvailablePair(
        IReadOnlyList<OperatingObservation> observations,
        PerformanceAtlasDimension preferredX,
        PerformanceAtlasDimension preferredY)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var available = AvailableDimensions(observations);
        var order = new[]
        {
            PerformanceAtlasDimension.PackagePower,
            PerformanceAtlasDimension.EffectiveClock,
            PerformanceAtlasDimension.ActiveCores,
            PerformanceAtlasDimension.CpuPressure
        };
        var pairs = order
            .SelectMany(x => order.Where(y => y != x).Select(y => (X: x, Y: y)))
            .Where(pair => HasUsablePair(observations, pair.X, pair.Y))
            .ToArray();

        if (pairs.Length == 0)
        {
            var explanation = available.Count switch
            {
                0 => "No usable observations are available for the Atlas yet.",
                1 => $"{FriendlyDimension(available[0])} is available, but the Atlas needs two metrics measured in the same observation.",
                _ => "No two available metrics currently overlap in the same observation, so PowerFlow will not draw an empty Atlas."
            };
            return new PerformanceAtlasDimensionPair(
                available.FirstOrDefault(),
                available.Skip(1).FirstOrDefault(),
                true,
                explanation,
                false);
        }

        if (preferredX != preferredY && HasUsablePair(observations, preferredX, preferredY))
            return new PerformanceAtlasDimensionPair(preferredX, preferredY, false, null, true);

        var chosen = pairs.FirstOrDefault(pair => pair.X == preferredX)
            is var xPreferred && xPreferred != default
                ? xPreferred
                : pairs.FirstOrDefault(pair => pair.Y == preferredY)
                    is var yPreferred && yPreferred != default
                        ? yPreferred
                        : pairs[0];
        var unavailable = !available.Contains(preferredX) ? preferredX : preferredY;
        return new PerformanceAtlasDimensionPair(
            chosen.X,
            chosen.Y,
            true,
            $"{FriendlyDimension(unavailable)} is not usable with the other selected metric right now; showing {FriendlyDimension(chosen.X)} × {FriendlyDimension(chosen.Y)} instead.",
            true);
    }
    public static PerformanceAtlasPointProjection? ProjectObservation(PerformanceAtlasData data, OperatingObservation observation)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(observation);
        var xValue = Value(observation, data.XAxis.Dimension);
        var yValue = Value(observation, data.YAxis.Dimension);
        if (!IsValid(xValue) || !IsValid(yValue)) return null;
        return new PerformanceAtlasPointProjection(
            NormalizeValue(xValue!.Value, data.XAxis),
            NormalizeValue(yValue!.Value, data.YAxis),
            xValue.Value,
            yValue.Value);
    }
    public static PerformanceAtlasData Build(
        IReadOnlyList<OperatingObservation> observations,
        PerformanceAtlasDimension x,
        PerformanceAtlasDimension y,
        int bins = 12,
        Func<OperatingObservation, double?>? efficiencySelector = null)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (x == y) throw new ArgumentException("Atlas dimensions must be distinct.", nameof(y));
        if (bins < 2) throw new ArgumentOutOfRangeException(nameof(bins), "Atlas requires at least two bins per axis.");

        var xAxis = CreateAxis(observations, x, bins);
        var yAxis = CreateAxis(observations, y, bins);
        var included = observations
            .Select((observation, index) => new IndexedObservation(index, observation, Value(observation, x), Value(observation, y)))
            .Where(item => IsValid(item.XValue) && IsValid(item.YValue))
            .ToArray();

        var grouped = included
            .GroupBy(item => new
            {
                X = Bin(item.XValue!.Value, xAxis),
                Y = Bin(item.YValue!.Value, yAxis)
            })
            .OrderBy(group => group.Key.X)
            .ThenBy(group => group.Key.Y)
            .ToArray();

        var maxCount = grouped.Length == 0 ? 1 : grouped.Max(group => group.Count());
        var cells = grouped.Select(group =>
        {
            var members = group.OrderBy(item => item.Index).ToArray();
            var evidence = efficiencySelector is null
                ? Array.Empty<double>()
                : members
                    .Select(item => efficiencySelector(item.Observation))
                    .Where(value => value is double raw && double.IsFinite(raw))
                    .Select(value => value!.Value)
                    .ToArray();
            return new PerformanceAtlasCell(
                group.Key.X,
                group.Key.Y,
                (double)members.Length / maxCount,
                members.Length,
                evidence.Length == 0 ? null : evidence.Average(),
                members.Select(item => item.Index).ToArray());
        }).ToArray();

        return new PerformanceAtlasData(
            xAxis,
            yAxis,
            cells,
            included.Select(item => item.Index).OrderBy(index => index).ToArray());
    }

    private static PerformanceAtlasAxis CreateAxis(
        IReadOnlyList<OperatingObservation> observations,
        PerformanceAtlasDimension dimension,
        int bins)
    {
        return dimension switch
        {
            PerformanceAtlasDimension.CpuPressure => new(dimension, "CPU PRESSURE", "%", 0, 100, bins),
            PerformanceAtlasDimension.PackagePower => new(dimension, "PACKAGE POWER", "W", 0,
                NiceCeiling(observations.Select(o => o.PackageWatts), 25, 25), bins),
            PerformanceAtlasDimension.EffectiveClock => new(dimension, "CPU PERFORMANCE", "%", 0,
                NiceCeiling(observations.Select(o => o.ProcessorPerformancePercent), 25, 125), bins),
            PerformanceAtlasDimension.ActiveCores => new(dimension, "CORES AWAKE", "cores", 0,
                Math.Max(1, observations.Select(o => (double?)(o.TotalCores ?? o.ActiveCores)).Where(IsValid).Select(v => v!.Value).DefaultIfEmpty(1).Max()), bins),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
    }

    private static double? Value(OperatingObservation observation, PerformanceAtlasDimension dimension) => dimension switch
    {
        PerformanceAtlasDimension.CpuPressure => observation.CpuPressurePercent,
        PerformanceAtlasDimension.PackagePower => observation.PackageWatts,
        PerformanceAtlasDimension.EffectiveClock => observation.ProcessorPerformancePercent,
        PerformanceAtlasDimension.ActiveCores => observation.ActiveCores,
        _ => null
    };

    private static int Bin(double value, PerformanceAtlasAxis axis)
    {
        var span = axis.DomainMax - axis.DomainMin;
        if (span <= 0) return 0;
        var normalized = Math.Clamp((value - axis.DomainMin) / span, 0, 1);
        return Math.Min(axis.BinCount - 1, (int)Math.Floor(normalized * axis.BinCount));
    }

    private static double NiceCeiling(IEnumerable<double?> values, double step, double minimum)
    {
        var valid = values.Where(IsValid).Select(value => value!.Value).ToArray();
        if (valid.Length == 0) return minimum;
        return Math.Max(minimum, Math.Ceiling(valid.Max() / step) * step);
    }

    private static double NormalizeValue(double value, PerformanceAtlasAxis axis)
    {
        var span = axis.DomainMax - axis.DomainMin;
        if (span <= 0) return 0;
        return Math.Clamp((value - axis.DomainMin) / span, 0, 1);
    }

    public static string FriendlyDimension(PerformanceAtlasDimension dimension) => dimension switch
    {
        PerformanceAtlasDimension.PackagePower => "Package Power",
        PerformanceAtlasDimension.EffectiveClock => "CPU Performance",
        PerformanceAtlasDimension.ActiveCores => "Cores Awake",
        PerformanceAtlasDimension.CpuPressure => "CPU Pressure",
        _ => dimension.ToString()
    };
    private static bool IsValid(double? value) => value is double raw && double.IsFinite(raw) && raw >= 0;

    private sealed record IndexedObservation(int Index, OperatingObservation Observation, double? XValue, double? YValue);
}
