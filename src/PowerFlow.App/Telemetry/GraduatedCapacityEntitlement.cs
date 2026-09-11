namespace PowerFlow.App.Telemetry;

public sealed record GraduatedCapacitySample(
    DateTimeOffset At,
    double RequestedCapacityPercent,
    double IdealCapacityPercent,
    double DeliveredCapacityPercent,
    double PressurePercent,
    double DemandPercent,
    double SustainedPressurePercent,
    double RampPercentPerSecond,
    double BurstAgeSeconds,
    double TargetSaturationPercent,
    double QueueReservePercent,
    string Driver);

public sealed record GraduatedCapacityTimelineData(IReadOnlyList<GraduatedCapacitySample> Samples)
{
    public static GraduatedCapacityTimelineData Empty { get; } = new(Array.Empty<GraduatedCapacitySample>());
}

public static class GraduatedCapacityEntitlementModel
{
    public const double MinimumEntitlementPercent = 12d;
    public const double SteadyTargetSaturationPercent = 60d;
    public const double MinimumTargetSaturationPercent = 40d;
    public const double StressFloorPercent = 40d;
    public const double StressCeilingPercent = 90d;
    public const double RampLookaheadSeconds = 1.5d;
    public const double SustainedTimeConstantSeconds = 6d;
    public const double BurstThresholdPercent = 70d;
    public const double BurstEscalationPercentPerSecond = 5d;
    public const double BurstEscalationMaximumPercent = 20d;
    public const double RiseSlewPercentPerSecond = 30d;
    public const double FallSlewPercentPerSecond = 6d;

    public static GraduatedCapacityTimelineData Build(
        IReadOnlyList<ContinuitySample>? history,
        DateTimeOffset windowStart,
        DateTimeOffset latest)
    {
        if (history is null || history.Count == 0) return GraduatedCapacityTimelineData.Empty;
        var source = history
            .Where(sample => sample.At <= latest && sample.DemandPressure is not null)
            .OrderBy(sample => sample.At)
            .ToArray();
        if (source.Length == 0) return GraduatedCapacityTimelineData.Empty;

        var projected = new List<GraduatedCapacitySample>(source.Length);
        DateTimeOffset? previousAt = null;
        double? previousPressure = null;
        double? previousRequested = null;
        double sustained = 0d;
        double burstAge = 0d;

        foreach (var sample in source)
        {
            var pressure = sample.DemandPressure!;
            var dt = previousAt is DateTimeOffset before
                ? Math.Clamp((sample.At - before).TotalSeconds, .05d, 30d)
                : 0d;
            var ramp = previousPressure is double prior && dt > 0
                ? Math.Clamp((pressure.PressurePercent - prior) / dt, -100d, 100d)
                : 0d;

            if (previousAt is null)
                sustained = pressure.PressurePercent;
            else
            {
                var alpha = 1d - Math.Exp(-dt / SustainedTimeConstantSeconds);
                sustained += alpha * (pressure.PressurePercent - sustained);
            }

            if (pressure.PressurePercent >= BurstThresholdPercent)
                burstAge = previousAt is null ? 0d : burstAge + dt;
            else
                burstAge = 0d;

            var projectedPressure = pressure.PressurePercent + Math.Max(0d, ramp) * RampLookaheadSeconds;
            var burstEquivalent = pressure.PressurePercent >= BurstThresholdPercent
                ? BurstThresholdPercent + Math.Min(BurstEscalationMaximumPercent, burstAge * BurstEscalationPercentPerSecond)
                : 0d;
            var stress = Math.Max(pressure.PressurePercent, Math.Max(sustained, Math.Max(projectedPressure, Math.Max(pressure.QueuePressurePercent, burstEquivalent))));
            var stressProgress = Math.Clamp((stress - StressFloorPercent) / (StressCeilingPercent - StressFloorPercent), 0d, 1d);
            var targetSaturation = SteadyTargetSaturationPercent - stressProgress * (SteadyTargetSaturationPercent - MinimumTargetSaturationPercent);

            var queueReserve = sample.LogicalProcessors is { Count: > 0 }
                ? Math.Clamp(pressure.QueueLength / sample.LogicalProcessors.Count * 100d, 0d, 25d)
                : 0d;
            var ideal = pressure.DemandPercent <= 0
                ? MinimumEntitlementPercent
                : pressure.DemandPercent / Math.Max(.01d, targetSaturation / 100d) + queueReserve;
            ideal = Math.Clamp(ideal, MinimumEntitlementPercent, 100d);

            double requested;
            if (previousRequested is not double previous || dt <= 0)
                requested = ideal;
            else
            {
                var delta = ideal - previous;
                var maximumRise = RiseSlewPercentPerSecond * dt;
                var maximumFall = FallSlewPercentPerSecond * dt;
                requested = previous + Math.Clamp(delta, -maximumFall, maximumRise);
            }
            requested = Math.Clamp(requested, MinimumEntitlementPercent, 100d);

            var delivered = Math.Clamp(pressure.AvailableCapacityPercent, 0d, 100d);
            var driver = pressure.QueuePressurePercent > 0d
                ? "QUEUE"
                : ramp >= 6d
                    ? "RAMP"
                    : burstAge >= 2d
                        ? "SUSTAINED"
                        : "DEMAND";

            projected.Add(new GraduatedCapacitySample(
                sample.At,
                requested,
                ideal,
                delivered,
                pressure.PressurePercent,
                pressure.DemandPercent,
                sustained,
                ramp,
                burstAge,
                targetSaturation,
                queueReserve,
                driver));

            previousAt = sample.At;
            previousPressure = pressure.PressurePercent;
            previousRequested = requested;
        }

        return new GraduatedCapacityTimelineData(projected.Where(sample => sample.At >= windowStart && sample.At <= latest).ToArray());
    }
}