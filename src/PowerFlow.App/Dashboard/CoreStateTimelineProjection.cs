using PowerFlow.App.Telemetry;
using PowerFlow.Windows.Activity;

namespace PowerFlow.App.Dashboard;

public sealed record CoreStateTimelineSample(
    DateTimeOffset At,
    int ActiveCores,
    int AwakeIdleCores,
    int ParkedCores,
    int TotalCores);

public sealed record CoreStateTimelineData(
    IReadOnlyList<CoreStateTimelineSample> Samples,
    int TotalCores)
{
    public static CoreStateTimelineData Empty { get; } = new(Array.Empty<CoreStateTimelineSample>(), 0);
}

public static class CoreStateTimelineProjection
{
    public static CoreStateTimelineData Build(
        IReadOnlyList<ContinuitySample>? history,
        DateTimeOffset windowStart,
        DateTimeOffset latest)
    {
        if (history is null || history.Count == 0) return CoreStateTimelineData.Empty;

        var samples = history
            .Where(sample => sample.At >= windowStart && sample.At <= latest && sample.LogicalProcessors is { Count: > 0 })
            .OrderBy(sample => sample.At)
            .Select(sample => Project(sample.At, sample.LogicalProcessors!))
            .Where(sample => sample.TotalCores > 0)
            .ToArray();

        return samples.Length == 0
            ? CoreStateTimelineData.Empty
            : new CoreStateTimelineData(samples, samples.Max(sample => sample.TotalCores));
    }

    public static CoreStateTimelineSample Project(DateTimeOffset at, IReadOnlyList<LogicalProcessorTelemetry> logicalProcessors)
    {
        ArgumentNullException.ThrowIfNull(logicalProcessors);
        var groups = logicalProcessors
            .Where(thread => thread.PhysicalCoreIndex >= 0)
            .GroupBy(thread => thread.PhysicalCoreIndex)
            .OrderBy(group => group.Key)
            .ToArray();

        var active = 0;
        var awakeIdle = 0;
        var parked = 0;
        foreach (var core in groups)
        {
            var threads = core.ToArray();
            if (threads.All(thread => thread.IsParked))
            {
                parked++;
                continue;
            }

            var isActive = threads.Any(thread =>
                !thread.IsParked &&
                thread.UtilizationPercent is double value &&
                double.IsFinite(value) &&
                value >= CoreThreadMapProjection.ActiveThresholdPercent);
            if (isActive) active++;
            else awakeIdle++;
        }

        return new CoreStateTimelineSample(at, active, awakeIdle, parked, groups.Length);
    }
}
