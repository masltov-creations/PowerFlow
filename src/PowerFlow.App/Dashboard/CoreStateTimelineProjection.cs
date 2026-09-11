using PowerFlow.App.Telemetry;
using PowerFlow.Windows.Activity;

namespace PowerFlow.App.Dashboard;

public sealed record CoreStateTimelineSample(
    DateTimeOffset At,
    int ActiveCores,
    int AwakeIdleCores,
    int ParkedCores,
    int TotalCores,
    IReadOnlyList<double>? ActiveCoreLoadsPercent = null,
    double AverageAwakeLoadPercent = 0,
    double? AverageAwakeFrequencyMhz = null,
    double? AverageAwakePercentOfMaximumFrequency = null,
    double? AverageAwakeProcessorPerformancePercent = null);

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

        var activeLoads = new List<double>();
        var awakeLoads = new List<double>();
        var awakeFrequency = new List<double>();
        var awakePercentMax = new List<double>();
        var awakePerformance = new List<double>();
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

            var load = Math.Clamp(threads
                .Where(thread => !thread.IsParked && thread.UtilizationPercent is double value && double.IsFinite(value) && value >= 0)
                .Sum(thread => Math.Max(0d, thread.UtilizationPercent!.Value)), 0d, 100d);
            awakeLoads.Add(load);

            var frequencies = threads.Where(thread => !thread.IsParked && thread.FrequencyMhz is double value && double.IsFinite(value) && value > 0)
                .Select(thread => thread.FrequencyMhz!.Value).ToArray();
            if (frequencies.Length > 0) awakeFrequency.Add(frequencies.Average());

            var percentMax = threads.Where(thread => !thread.IsParked && thread.PercentOfMaximumFrequency is double value && double.IsFinite(value) && value > 0)
                .Select(thread => thread.PercentOfMaximumFrequency!.Value).ToArray();
            if (percentMax.Length > 0) awakePercentMax.Add(percentMax.Average());

            var processorPerformance = threads.Where(thread => !thread.IsParked && thread.ProcessorPerformancePercent is double value && double.IsFinite(value) && value > 0)
                .Select(thread => thread.ProcessorPerformancePercent!.Value).ToArray();
            if (processorPerformance.Length > 0) awakePerformance.Add(processorPerformance.Average());

            if (load >= CoreThreadMapProjection.ActiveThresholdPercent) activeLoads.Add(load);
            else awakeIdle++;
        }

        activeLoads.Sort((a, b) => b.CompareTo(a));
        return new CoreStateTimelineSample(
            at,
            activeLoads.Count,
            awakeIdle,
            parked,
            groups.Length,
            activeLoads,
            awakeLoads.Count == 0 ? 0 : awakeLoads.Average(),
            awakeFrequency.Count == 0 ? null : awakeFrequency.Average(),
            awakePercentMax.Count == 0 ? null : awakePercentMax.Average(),
            awakePerformance.Count == 0 ? null : awakePerformance.Average());
    }
}