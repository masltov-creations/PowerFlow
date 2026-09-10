using PowerFlow.Windows.Activity;

namespace PowerFlow.App.Telemetry;

public sealed record DemandPressureTelemetry(
    double PressurePercent,
    double DemandPercent,
    double AvailableCapacityPercent,
    double CapacitySaturationPercent,
    double QueuePressurePercent,
    double QueueLength,
    int AwakeCores,
    int TotalCores,
    string Driver);

public static class DemandPressureModel
{
    public static DemandPressureTelemetry? Project(DashboardTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        if (telemetry.LogicalProcessors is not { Count: > 0 } logical) return null;

        var valid = logical
            .Where(thread => thread.PhysicalCoreIndex >= 0)
            .ToArray();
        if (valid.Length == 0) return null;

        var cores = valid
            .GroupBy(thread => thread.PhysicalCoreIndex)
            .OrderBy(group => group.Key)
            .ToArray();
        var awakeCores = cores.Count(core => core.Any(thread => !thread.IsParked));

        // Processor Utility represents delivered work relative to nominal processor performance.
        // Summed over all logical processors, then divided by logical count, it gives machine demand
        // on a full-machine nominal-capacity scale.
        var usedCapacityUnits = valid.Sum(thread =>
            thread.UtilizationPercent is double utility && double.IsFinite(utility) && utility > 0
                ? Math.Max(0d, utility)
                : 0d);
        var demand = Math.Clamp(usedCapacityUnits / valid.Length, 0d, 100d);

        // Available compute capacity is the sum of each unparked logical processor's current
        // performance state. Missing frequency telemetry falls back to 100% so missing data cannot
        // manufacture artificial pressure. This intentionally measures observed available capacity,
        // not a configured plan ceiling.
        var availableCapacityUnits = valid
            .Where(thread => !thread.IsParked)
            .Sum(thread =>
                thread.PercentOfMaximumFrequency is double percent && double.IsFinite(percent) && percent > 0
                    ? Math.Clamp(percent, 1d, 200d)
                    : 100d);
        var availableCapacity = Math.Clamp(availableCapacityUnits / valid.Length, 0d, 200d);
        var capacitySaturation = availableCapacityUnits <= 0
            ? (usedCapacityUnits > 0 ? 100d : 0d)
            : Math.Clamp(usedCapacityUnits / availableCapacityUnits * 100d, 0d, 100d);

        var queueLength = telemetry.ProcessorQueueLength is double queue && double.IsFinite(queue) && queue > 0 ? queue : 0d;
        var queuePressure = awakeCores == 0
            ? (queueLength > 0 ? 100d : 0d)
            : Math.Clamp(queueLength / awakeCores * 100d, 0d, 100d);

        var pressure = Math.Max(demand, Math.Max(capacitySaturation, queuePressure));
        var driver = pressure == queuePressure && queuePressure >= capacitySaturation && queuePressure >= demand
            ? "QUEUE"
            : pressure == capacitySaturation && capacitySaturation >= demand
                ? "SATURATION"
                : "DEMAND";

        return new DemandPressureTelemetry(
            pressure,
            demand,
            availableCapacity,
            capacitySaturation,
            queuePressure,
            queueLength,
            awakeCores,
            cores.Length,
            driver);
    }
}