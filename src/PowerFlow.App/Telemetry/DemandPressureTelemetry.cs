using PowerFlow.Windows.Activity;

namespace PowerFlow.App.Telemetry;

public sealed record DemandPressureTelemetry(
    double PressurePercent,
    double DemandPercent,
    double AwakeSaturationPercent,
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

        var cores = logical
            .Where(thread => thread.PhysicalCoreIndex >= 0)
            .GroupBy(thread => thread.PhysicalCoreIndex)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var threads = group.ToArray();
                var parked = threads.All(thread => thread.IsParked);
                var utility = threads
                    .Where(thread => !thread.IsParked && thread.UtilizationPercent is double value && double.IsFinite(value) && value >= 0)
                    .Sum(thread => Math.Max(0d, thread.UtilizationPercent!.Value));
                return new { Parked = parked, Load = Math.Clamp(utility, 0d, 100d) };
            })
            .ToArray();

        if (cores.Length == 0) return null;
        var awake = cores.Where(core => !core.Parked).ToArray();
        var totalDemand = cores.Sum(core => core.Load);
        var demand = Math.Clamp(totalDemand / cores.Length, 0d, 100d);
        var saturation = awake.Length == 0 ? 0d : Math.Clamp(awake.Sum(core => core.Load) / awake.Length, 0d, 100d);
        var queueLength = telemetry.ProcessorQueueLength is double queue && double.IsFinite(queue) && queue > 0 ? queue : 0d;
        var queuePressure = awake.Length == 0
            ? (queueLength > 0 ? 100d : 0d)
            : Math.Clamp(queueLength / awake.Length * 100d, 0d, 100d);

        var pressure = Math.Max(demand, Math.Max(saturation, queuePressure));
        var driver = pressure == queuePressure && queuePressure >= saturation && queuePressure >= demand
            ? "QUEUE"
            : pressure == saturation && saturation >= demand
                ? "SATURATION"
                : "DEMAND";

        return new DemandPressureTelemetry(
            pressure,
            demand,
            saturation,
            queuePressure,
            queueLength,
            awake.Length,
            cores.Length,
            driver);
    }
}