using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;

namespace PowerFlow.App.Tray;

public enum TrayIconMotionKind
{
    None,
    Promote,
    Settle,
    Hold
}

public sealed record TrayIconMotionPlan(TrayIconMotionKind Kind, IReadOnlyList<int> FrameIndices, TimeSpan FrameInterval)
{
    public TimeSpan Duration => TimeSpan.FromTicks(FrameInterval.Ticks * FrameIndices.Count);
    public static TrayIconMotionPlan None { get; } = new(TrayIconMotionKind.None, Array.Empty<int>(), TimeSpan.Zero);
}

public static class TrayIconMotionPolicy
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(100);

    public static TrayIconMotionPlan Decide(ControllerSnapshot previous, ControllerSnapshot current, PowerFlowOperatingMode? manualMode)
        => Decide(previous, current, null, manualMode);

    public static TrayIconMotionPlan Decide(ControllerSnapshot previous, ControllerSnapshot current, PowerFlowOperatingMode? previousManualMode, PowerFlowOperatingMode? currentManualMode)
    {
        var from = SettledFrame(previous, previousManualMode);
        var to = SettledFrame(current, currentManualMode);
        var authorityChanged = previous.IsLatched != current.IsLatched
            || !string.Equals(previous.LatchType, current.LatchType, StringComparison.OrdinalIgnoreCase)
            || previousManualMode != currentManualMode;
        if (from == to && !authorityChanged) return TrayIconMotionPlan.None;

        var kind = to > from ? TrayIconMotionKind.Promote : to < from ? TrayIconMotionKind.Settle : TrayIconMotionKind.Hold;
        return new TrayIconMotionPlan(kind, Sequence(from, to, kind), FrameInterval);
    }

    public static int SettledFrame(ControllerSnapshot snapshot, PowerFlowOperatingMode? manualMode)
    {
        if (manualMode is not null)
            return manualMode.Value switch
            {
                PowerFlowOperatingMode.Saver => 0,
                PowerFlowOperatingMode.Balanced => 1,
                PowerFlowOperatingMode.BalancedPerformance => 3,
                PowerFlowOperatingMode.Performance => 4,
                PowerFlowOperatingMode.Ultra => 4,
                _ => 2
            };
        return snapshot.State switch
        {
            PowerState.PowerSaver => 0,
            PowerState.HighPerformance => 4,
            _ => 2
        };
    }

    private static IReadOnlyList<int> Sequence(int from, int to, TrayIconMotionKind kind)
    {
        if (kind == TrayIconMotionKind.Hold)
        {
            var away = to <= 2 ? Math.Min(4, to + 1) : Math.Max(0, to - 1);
            return new[] { to, away, to, away, to };
        }

        var frames = new List<int> { from };
        var direction = Math.Sign(to - from);
        for (var frame = from + direction; frame != to + direction; frame += direction) frames.Add(frame);
        if (frames.Count < 4)
        {
            var overshoot = Math.Clamp(to + direction, 0, 4);
            frames.Add(overshoot == to ? Math.Clamp(to - direction, 0, 4) : overshoot);
            frames.Add(to);
        }
        while (frames.Count < 4) frames.Insert(0, from);
        if (frames.Count > 7) frames = frames.Take(6).Append(to).ToList();
        if (frames[^1] != to) frames.Add(to);
        return frames;
    }
}