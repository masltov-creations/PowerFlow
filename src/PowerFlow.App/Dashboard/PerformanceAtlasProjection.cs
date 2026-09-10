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

public sealed record PerformanceAtlasData(
    PerformanceAtlasAxis XAxis,
    PerformanceAtlasAxis YAxis,
    IReadOnlyList<PerformanceAtlasCell> Cells,
    IReadOnlyList<int> IncludedObservationIndices);

public static class PerformanceAtlasProjection
{
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
            PerformanceAtlasDimension.EffectiveClock => new(dimension, "EFFECTIVE CLOCK", "MHz", 0,
                NiceCeiling(observations.Select(o => o.EffectiveClockMhz), 500, 1000), bins),
            PerformanceAtlasDimension.ActiveCores => new(dimension, "CORES AWAKE", "cores", 0,
                Math.Max(1, observations.Select(o => (double?)(o.TotalCores ?? o.ActiveCores)).Where(IsValid).Select(v => v!.Value).DefaultIfEmpty(1).Max()), bins),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
    }

    private static double? Value(OperatingObservation observation, PerformanceAtlasDimension dimension) => dimension switch
    {
        PerformanceAtlasDimension.CpuPressure => observation.CpuPressurePercent,
        PerformanceAtlasDimension.PackagePower => observation.PackageWatts,
        PerformanceAtlasDimension.EffectiveClock => observation.EffectiveClockMhz,
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

    private static bool IsValid(double? value) => value is double raw && double.IsFinite(raw) && raw >= 0;

    private sealed record IndexedObservation(int Index, OperatingObservation Observation, double? XValue, double? YValue);
}
