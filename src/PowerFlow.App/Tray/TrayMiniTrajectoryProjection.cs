using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;
using PowerFlow.Windows.Activity;

namespace PowerFlow.App.Tray;

public sealed record TrayMiniTrajectoryModel(
    TrajectoryModel Trajectory,
    IReadOnlyList<ContinuitySample> Samples,
    IReadOnlyList<TrajectoryTransitionMarker> Transitions,
    string StateLabel,
    string ModeBadge,
    string CpuLabel,
    string WattsLabel,
    string FrequencyLabel,
    string DirectionLabel,
    string NextAction);

public static class TrayMiniTrajectoryProjection
{
    public static TrayMiniTrajectoryModel Create(
        IReadOnlyList<ContinuitySample> continuity,
        ControllerSnapshot snapshot,
        PowerFlowConfig config,
        DashboardTelemetry? telemetry,
        int maxSamples = 48)
    {
        var samples = continuity.OrderBy(x => x.At).TakeLast(Math.Max(2, maxSamples)).ToArray();
        var trajectory = TrajectoryProjection.Create(samples, snapshot, config);
        var state = StateName(snapshot.State);
        var mode = snapshot.LatchType switch
        {
            "Manual" => "MANUAL LOCK",
            "Game" => "GAME LOCK",
            _ => "AUTO"
        };
        var watts = telemetry?.PackageWatts is double w ? $"{w:0.0} W" : "—";
        var frequency = telemetry?.AverageMhz is double mhz ? $"{mhz / 1000d:0.00}" : "—";
        var direction = trajectory.Direction switch
        {
            TrajectoryDirection.TowardBalanced => "→ BALANCED",
            TrajectoryDirection.TowardSaver => "→ SAVER",
            TrajectoryDirection.HoldPerformance => "GAME HOLD",
            TrajectoryDirection.HoldManual => "MANUAL HOLD",
            _ => "STEADY"
        };
        return new(
            trajectory,
            samples,
            trajectory.Transitions,
            state,
            mode,
            $"{snapshot.CpuPercent:0.0}%",
            watts,
            frequency,
            direction,
            trajectory.NextAction);
    }

    private static string StateName(PowerState state) => state switch
    {
        PowerState.PowerSaver => "POWER SAVER",
        PowerState.Balanced => "BALANCED",
        PowerState.HighPerformance => "PERFORMANCE",
        _ => state.ToString().ToUpperInvariant()
    };
}
