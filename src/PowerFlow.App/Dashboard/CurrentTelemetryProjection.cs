using PowerFlow.Core.Envelope;

namespace PowerFlow.App.Dashboard;

public sealed record CurrentTelemetrySnapshot(
    OperatingObservation Latest,
    double? PackageWatts,
    double? EffectiveClockMhz,
    double? ProcessorPerformancePercent,
    int? ActiveCores,
    int? TotalCores);

public static class CurrentTelemetryProjection
{
    public static CurrentTelemetrySnapshot? Resolve(
        IReadOnlyList<OperatingObservation> observations,
        TimeSpan richMetricFreshness)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0) return null;

        var freshness = richMetricFreshness < TimeSpan.Zero ? TimeSpan.Zero : richMetricFreshness;
        var ordered = observations.OrderByDescending(observation => observation.At).ToArray();
        var latest = ordered[0];
        var cutoff = latest.At - freshness;
        var recent = ordered.Where(observation => observation.At >= cutoff).ToArray();

        var power = recent.FirstOrDefault(observation => observation.PackageWatts is not null)?.PackageWatts;
        var clock = recent.FirstOrDefault(observation => observation.EffectiveClockMhz is not null)?.EffectiveClockMhz;
        var performance = recent.FirstOrDefault(observation => observation.ProcessorPerformancePercent is not null)?.ProcessorPerformancePercent;
        var cores = recent.FirstOrDefault(observation => observation.ActiveCores is not null);
        var total = cores?.TotalCores ?? recent.FirstOrDefault(observation => observation.TotalCores is not null)?.TotalCores;

        return new CurrentTelemetrySnapshot(latest, power, clock, performance, cores?.ActiveCores, total);
    }
}