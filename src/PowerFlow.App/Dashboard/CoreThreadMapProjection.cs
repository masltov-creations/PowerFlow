using PowerFlow.Windows.Activity;

namespace PowerFlow.App.Dashboard;

public enum ThreadOccupancyState
{
    Parked,
    AwakeIdle,
    Active
}

public sealed record CoreThreadCell(
    int LogicalProcessorIndex,
    int PhysicalCoreIndex,
    ThreadOccupancyState State,
    double? UtilizationPercent);

public sealed record CoreThreadColumn(int PhysicalCoreIndex, IReadOnlyList<CoreThreadCell> Threads);

public sealed record CoreThreadMapSnapshot(
    IReadOnlyList<CoreThreadColumn> Cores,
    int TotalThreads,
    int AwakeThreads,
    int ActiveThreads,
    int ParkedThreads,
    int AwakeCores)
{
    public static CoreThreadMapSnapshot Empty { get; } = new(Array.Empty<CoreThreadColumn>(), 0, 0, 0, 0, 0);
}

public static class CoreThreadMapProjection
{
    public const double ActiveThresholdPercent = 5d;

    public static CoreThreadMapSnapshot Build(IReadOnlyList<LogicalProcessorTelemetry>? logicalProcessors)
    {
        if (logicalProcessors is null || logicalProcessors.Count == 0) return CoreThreadMapSnapshot.Empty;

        var cells = logicalProcessors
            .Where(thread => thread.LogicalProcessorIndex >= 0 && thread.PhysicalCoreIndex >= 0)
            .OrderBy(thread => thread.PhysicalCoreIndex)
            .ThenBy(thread => thread.LogicalProcessorIndex)
            .Select(thread => new CoreThreadCell(
                thread.LogicalProcessorIndex,
                thread.PhysicalCoreIndex,
                Classify(thread),
                thread.UtilizationPercent is double value && double.IsFinite(value) ? Math.Clamp(value, 0d, 100d) : null))
            .ToArray();

        if (cells.Length == 0) return CoreThreadMapSnapshot.Empty;

        var cores = cells
            .GroupBy(cell => cell.PhysicalCoreIndex)
            .OrderBy(group => group.Key)
            .Select(group => new CoreThreadColumn(group.Key, group.OrderBy(cell => cell.LogicalProcessorIndex).ToArray()))
            .ToArray();

        return new CoreThreadMapSnapshot(
            cores,
            cells.Length,
            cells.Count(cell => cell.State != ThreadOccupancyState.Parked),
            cells.Count(cell => cell.State == ThreadOccupancyState.Active),
            cells.Count(cell => cell.State == ThreadOccupancyState.Parked),
            cores.Count(core => core.Threads.Any(thread => thread.State != ThreadOccupancyState.Parked)));
    }

    private static ThreadOccupancyState Classify(LogicalProcessorTelemetry thread)
    {
        if (thread.IsParked) return ThreadOccupancyState.Parked;
        return thread.UtilizationPercent is double utilization && double.IsFinite(utilization) && utilization >= ActiveThresholdPercent
            ? ThreadOccupancyState.Active
            : ThreadOccupancyState.AwakeIdle;
    }
}
