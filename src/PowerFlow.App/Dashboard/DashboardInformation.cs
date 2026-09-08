using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;

namespace PowerFlow.App.Dashboard;

public sealed record DashboardSample(DateTimeOffset At, double CpuPercent, double? PackageWatts, double? AverageMhz, PowerState State);

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
            var trigger = string.IsNullOrWhiteSpace(snapshot.TriggerApplication) ? snapshot.LatchType ?? "latch" : snapshot.TriggerApplication;
            return new DecisionMeterState(
                "PERFORMANCE LATCH",
                100,
                config.QuietThresholdPercent,
                config.CpuPromotionThresholdPercent,
                $"Performance is locked by {trigger}; utilization is ignored until the latch releases.");
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
    private const double WindowSeconds = 59d;

    public static double NormalizedX(DateTimeOffset at, DateTimeOffset latest)
    {
        var windowStart = latest.AddSeconds(-WindowSeconds);
        return Math.Clamp((at - windowStart).TotalSeconds / WindowSeconds, 0, 1);
    }
}
