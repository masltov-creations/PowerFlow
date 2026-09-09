using PowerFlow.App.Controller;
using PowerFlow.App.Telemetry;
using PowerFlow.Core.Policy;
using PowerFlow.Core.Rules;

namespace PowerFlow.App.Dashboard;

public enum TrajectoryDirection
{
    Stay,
    TowardSaver,
    TowardBalanced,
    HoldPerformance,
    HoldManual
}

public sealed record TrajectoryStateSegment(DateTimeOffset From, DateTimeOffset To, PowerState State);
public sealed record TrajectoryGap(DateTimeOffset From, DateTimeOffset To);
public sealed record TrajectoryTransitionMarker(DateTimeOffset At, PowerState From, PowerState To, string Reason, bool Success);
public sealed record TrajectoryRail(double ThresholdPercent, TimeSpan HoldTime, string Label);
public sealed record TrajectoryNow(PowerState State, double CpuPercent, double ThresholdProgress, bool IsLatched, string? LatchType, string Reason);
public sealed record TrajectoryModel(
    IReadOnlyList<ContinuitySample> Samples,
    IReadOnlyList<TrajectoryStateSegment> StateSegments,
    IReadOnlyList<TrajectoryGap> Gaps,
    IReadOnlyList<TrajectoryTransitionMarker> Transitions,
    TrajectoryRail QuietRail,
    TrajectoryRail PromoteRail,
    TrajectoryNow Now,
    TrajectoryDirection Direction,
    string NextAction);

public static class TrajectoryProjection
{
    private static readonly TimeSpan GapThreshold = TimeSpan.FromSeconds(10);

    public static TrajectoryModel Create(IReadOnlyList<ContinuitySample> samples, ControllerSnapshot snapshot, PowerFlowConfig config)
    {
        var ordered = samples.OrderBy(x => x.At).ToArray();
        var segments = new List<TrajectoryStateSegment>();
        var gaps = new List<TrajectoryGap>();

        if (ordered.Length > 0)
        {
            var segmentStart = ordered[0].At;
            var segmentEnd = ordered[0].At;
            var segmentState = ordered[0].State;
            for (var i = 1; i < ordered.Length; i++)
            {
                var previous = ordered[i - 1];
                var current = ordered[i];
                var gap = current.At - previous.At > GapThreshold;
                var stateChanged = current.State != segmentState;
                if (gap || stateChanged)
                {
                    segments.Add(new TrajectoryStateSegment(segmentStart, segmentEnd, segmentState));
                    if (gap) gaps.Add(new TrajectoryGap(previous.At, current.At));
                    segmentStart = current.At;
                    segmentState = current.State;
                }
                segmentEnd = current.At;
            }
            segments.Add(new TrajectoryStateSegment(segmentStart, segmentEnd, segmentState));
        }

        var transitions = snapshot.History
            .Where(x => x.From != x.To || !x.Success)
            .Select(x => new TrajectoryTransitionMarker(x.At, x.From, x.To, x.Reason, x.Success))
            .OrderBy(x => x.At)
            .ToArray();

        var direction = DetermineDirection(snapshot, config);
        return new TrajectoryModel(
            ordered,
            segments,
            gaps,
            transitions,
            new TrajectoryRail(config.QuietThresholdPercent, config.QuietWindow, $"QUIET {config.QuietThresholdPercent:0.#}%"),
            new TrajectoryRail(config.CpuPromotionThresholdPercent, config.CpuPromotionWindow, $"PROMOTE {config.CpuPromotionThresholdPercent:0.#}%"),
            new TrajectoryNow(snapshot.State, snapshot.CpuPercent, Math.Clamp(snapshot.ThresholdProgress, 0, 1), snapshot.IsLatched, snapshot.LatchType, snapshot.Reason),
            direction,
            NextAction(snapshot, config, direction));
    }

    private static TrajectoryDirection DetermineDirection(ControllerSnapshot snapshot, PowerFlowConfig config)
    {
        if (snapshot.IsLatched && string.Equals(snapshot.LatchType, "Game", StringComparison.OrdinalIgnoreCase)) return TrajectoryDirection.HoldPerformance;
        if (snapshot.IsLatched && string.Equals(snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase)) return TrajectoryDirection.HoldManual;
        if (snapshot.State == PowerState.PowerSaver && snapshot.CpuPercent >= config.CpuPromotionThresholdPercent) return TrajectoryDirection.TowardBalanced;
        if (snapshot.State == PowerState.Balanced && snapshot.CpuPercent <= config.QuietThresholdPercent) return TrajectoryDirection.TowardSaver;
        return TrajectoryDirection.Stay;
    }

    private static string NextAction(ControllerSnapshot snapshot, PowerFlowConfig config, TrajectoryDirection direction) => direction switch
    {
        TrajectoryDirection.HoldPerformance => "Performance held until tracked game exits",
        TrajectoryDirection.HoldManual => $"{StateName(snapshot.State)} held until AUTO releases the manual lock",
        TrajectoryDirection.TowardBalanced => $"Qualifying for Balanced: CPU is above {config.CpuPromotionThresholdPercent:0.#}% ({config.CpuPromotionWindow.TotalSeconds:0.#}s hold)",
        TrajectoryDirection.TowardSaver => $"Qualifying for Power Saver: CPU is below {config.QuietThresholdPercent:0.#}% ({config.QuietWindow.TotalSeconds:0.#}s hold)",
        _ when snapshot.State == PowerState.PowerSaver => $"Balanced above {config.CpuPromotionThresholdPercent:0.#}% for {config.CpuPromotionWindow.TotalSeconds:0.#}s",
        _ when snapshot.State == PowerState.Balanced => $"Power Saver below {config.QuietThresholdPercent:0.#}% for {config.QuietWindow.TotalSeconds:0.#}s",
        _ => "Performance active"
    };

    private static string StateName(PowerState state) => state switch
    {
        PowerState.PowerSaver => "Power Saver",
        PowerState.Balanced => "Balanced",
        PowerState.HighPerformance => "Performance",
        _ => state.ToString()
    };
}
