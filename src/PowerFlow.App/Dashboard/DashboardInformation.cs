using PowerFlow.App.Controller;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;

namespace PowerFlow.App.Dashboard;

public sealed record DashboardSample(
    DateTimeOffset At,
    double CpuPercent,
    double? PackageWatts,
    double? AverageMhz,
    PowerState State,
    string? Actor = null,
    int? ActiveCores = null,
    int? TotalCores = null,
    double? PressurePercent = null,
    double? ProcessorPerformancePercent = null);

public static class OperatingObservationProjection
{
    public static OperatingObservation FromDashboardSample(DashboardSample sample) => new(
        sample.At,
        sample.PressurePercent ?? sample.CpuPercent,
        sample.PackageWatts,
        sample.AverageMhz,
        sample.ActiveCores,
        sample.TotalCores,
        sample.State switch
        {
            PowerState.PowerSaver => EnvelopeZone.Eco,
            PowerState.Balanced => EnvelopeZone.Efficient,
            PowerState.HighPerformance => EnvelopeZone.Boost,
            _ => EnvelopeZone.Efficient
        },
        sample.Actor,
        EnvelopeDecisionKind.None,
        sample.ProcessorPerformancePercent);
}
public sealed record DashboardRuleCard(string Title, string Condition, string Target, string Status, double Activity);

public static class DashboardRuleProjection
{
    public static IReadOnlyList<DashboardRuleCard> Create(PowerFlowConfig config, ControllerSnapshot snapshot)
    {
        var promoteActive = !snapshot.IsLatched && snapshot.State == PowerState.PowerSaver && snapshot.CpuPercent >= config.CpuPromotionThresholdPercent;
        var quietActive = !snapshot.IsLatched && snapshot.State == PowerState.Balanced && snapshot.CpuPercent <= config.QuietThresholdPercent;
        var gameStatus = snapshot.IsLatched && string.Equals(snapshot.LatchType, "Game", StringComparison.OrdinalIgnoreCase) ? "LOCKED" : "ARMED";

        return new[]
        {
            new DashboardRuleCard(
                "SUSTAINED CPU",
                $"CPU > {config.CpuPromotionThresholdPercent:0.#}% for {config.CpuPromotionWindow.TotalSeconds:0.#}s",
                "BALANCED",
                promoteActive ? "ACTIVE" : snapshot.State == PowerState.Balanced ? "SATISFIED" : "STANDBY",
                Math.Clamp(snapshot.CpuPercent / Math.Max(1, config.CpuPromotionThresholdPercent), 0, 1)),
            new DashboardRuleCard(
                "QUIET RETURN",
                $"CPU < {config.QuietThresholdPercent:0.#}% for {config.QuietWindow.TotalSeconds:0.#}s",
                "POWER SAVER",
                quietActive ? "ACTIVE" : "STANDBY",
                quietActive ? Math.Clamp(1 - snapshot.CpuPercent / Math.Max(1, config.QuietThresholdPercent), 0, 1) : 0),
            new DashboardRuleCard(
                "GAME / APP",
                "Process lifecycle match",
                "PERFORMANCE LOCK",
                gameStatus,
                gameStatus == "LOCKED" ? 1 : 0)
        };
    }
}

public sealed record DecisionMeterState(
    string Title,
    double ValuePercent,
    double QuietMarkerPercent,
    double PromotionMarkerPercent,
    string Explanation);

public static class DecisionMeterProjection
{
    public static DecisionMeterState Create(PowerFlowConfig config, ControllerSnapshot snapshot)
    {
        if (snapshot.IsLatched)
        {
            var trigger = string.IsNullOrWhiteSpace(snapshot.TriggerApplication) ? snapshot.LatchType ?? "lock" : snapshot.TriggerApplication;
            if (string.Equals(snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase))
            {
                var state = snapshot.State switch
                {
                    PowerState.PowerSaver => "Power Saver",
                    PowerState.Balanced => "Balanced",
                    PowerState.HighPerformance => "High Performance",
                    _ => snapshot.State.ToString()
                };
                return new DecisionMeterState(
                    "MANUAL LOCK",
                    snapshot.State == PowerState.HighPerformance ? 100 : snapshot.CpuPercent,
                    config.QuietThresholdPercent,
                    config.CpuPromotionThresholdPercent,
                    $"{state} is manually locked by {trigger}; automatic CPU policy is paused until you release the lock.");
            }

            return new DecisionMeterState(
                "PERFORMANCE LATCH",
                100,
                config.QuietThresholdPercent,
                config.CpuPromotionThresholdPercent,
                $"Performance is locked by {trigger}; utilization is ignored until the game exits.");
        }

        return new DecisionMeterState(
            "CPU PRESSURE",
            Math.Clamp(snapshot.CpuPercent, 0, 100),
            config.QuietThresholdPercent,
            config.CpuPromotionThresholdPercent,
            $"Quiet return below {config.QuietThresholdPercent:0.#}%. Promote to Balanced at {config.CpuPromotionThresholdPercent:0.#}% after {config.CpuPromotionWindow.TotalSeconds:0.#}s.");
    }
}
public static class TelemetryPlotProjection
{
    public static double NormalizedX(DateTimeOffset at, DateTimeOffset latest) => NormalizedX(at, latest, 60);

    public static double NormalizedX(DateTimeOffset at, DateTimeOffset latest, double windowSeconds)
    {
        var seconds = Math.Max(1, windowSeconds);
        var windowStart = latest.AddSeconds(-seconds);
        return Math.Clamp((at - windowStart).TotalSeconds / seconds, 0, 1);
    }

    public static DashboardSample? FindNearestSample(IReadOnlyList<DashboardSample> samples, double normalizedX, DateTimeOffset latest, double windowSeconds)
    {
        if (samples.Count == 0) return null;
        var seconds = Math.Max(1, windowSeconds);
        var target = latest.AddSeconds(-(1 - Math.Clamp(normalizedX, 0, 1)) * seconds);
        var start = latest.AddSeconds(-seconds);
        return samples
            .Where(sample => sample.At >= start && sample.At <= latest)
            .OrderBy(sample => Math.Abs((sample.At - target).TotalMilliseconds))
            .FirstOrDefault();
    }

    public static double? NormalizedTransitionX(TransitionRecord transition, DateTimeOffset latest, double windowSeconds)
    {
        var seconds = Math.Max(1, windowSeconds);
        var start = latest.AddSeconds(-seconds);
        if (transition.At < start || transition.At > latest) return null;
        return NormalizedX(transition.At, latest, seconds);
    }
}
public readonly record struct PlotPoint(double X, double Y);
public readonly record struct SmoothCurveSegment(PlotPoint Control1, PlotPoint Control2, PlotPoint End);

public static class SmoothGraphProjection
{
    public static IReadOnlyList<SmoothCurveSegment> CreateSegments(IReadOnlyList<PlotPoint> points)
    {
        if (points.Count < 2) return Array.Empty<SmoothCurveSegment>();
        var result = new List<SmoothCurveSegment>(points.Count - 1);
        const double factor = 1d / 6d;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = i == 0 ? points[i] : points[i - 1];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i + 2 < points.Count ? points[i + 2] : p2;
            var c1 = new PlotPoint(
                Math.Clamp(p1.X + (p2.X - p0.X) * factor, p1.X, p2.X),
                p1.Y + (p2.Y - p0.Y) * factor);
            var c2 = new PlotPoint(
                Math.Clamp(p2.X - (p3.X - p1.X) * factor, p1.X, p2.X),
                p2.Y - (p3.Y - p1.Y) * factor);
            result.Add(new SmoothCurveSegment(c1, c2, p2));
        }
        return result;
    }
}
