using PowerFlow.Core.Policy;

namespace PowerFlow.App.Dashboard;

public enum TrajectoryLensKind
{
    Sample,
    Now,
    Transition,
    QuietRail,
    PromoteRail,
    Mode
}

public sealed record TrajectoryLensModel(
    TrajectoryLensKind Kind,
    string Title,
    string Primary,
    string Secondary,
    string Detail,
    string? Key = null);

public static class TrajectoryLensProjection
{
    public static TrajectoryLensModel ForSample(HoverTelemetry sample)
    {
        var watts = sample.PackageWatts is double w ? $"{w:0.0} W" : "— W";
        var ghz = sample.AverageMhz is double mhz ? $"{mhz / 1000d:0.00} GHz" : "— GHz";
        return new(
            TrajectoryLensKind.Sample,
            $"{StateName(sample.State)} · {sample.At:HH:mm:ss}",
            $"CPU {sample.CpuPercent:0.0}%",
            $"{watts} · {ghz}",
            "Observed telemetry at this point in the trajectory");
    }

    public static TrajectoryLensModel ForNow(TrajectoryModel model) => new(
        TrajectoryLensKind.Now,
        $"NOW · {StateName(model.Now.State)}",
        $"CPU {model.Now.CpuPercent:0.0}%",
        $"Qualification {model.Now.ThresholdProgress * 100:0}%",
        model.NextAction,
        "now");

    public static TrajectoryLensModel ForTransition(TrajectoryTransitionMarker marker)
    {
        var prefix = marker.Success ? "CHANGE" : "FAILED";
        return new(
            TrajectoryLensKind.Transition,
            $"{prefix} · {StateName(marker.From)} → {StateName(marker.To)}",
            marker.At.ToLocalTime().ToString("HH:mm:ss"),
            marker.Success ? "Applied" : "Not applied",
            marker.Reason,
            $"{marker.At.UtcTicks}:{marker.From}:{marker.To}:{marker.Success}");
    }

    public static TrajectoryLensModel ForRail(TrajectoryRail rail, bool promote)
    {
        var kind = promote ? TrajectoryLensKind.PromoteRail : TrajectoryLensKind.QuietRail;
        var direction = promote ? "Promotes to Balanced after sustained demand" : "Returns toward Power Saver after sustained quiet";
        return new(kind, rail.Label, $"{rail.ThresholdPercent:0.#}% CPU", $"Hold {rail.HoldTime.TotalSeconds:0.#}s", $"{direction} · {rail.HoldTime.TotalSeconds:0.#}s hold", promote ? "promote" : "quiet");
    }

    public static TrajectoryLensModel ForMode(PowerState state, bool isCurrent, bool isManual) => new(
        TrajectoryLensKind.Mode,
        StateName(state),
        isCurrent ? "CURRENT MODE" : "AVAILABLE MODE",
        isManual ? "Manual lock active" : "AUTO policy available",
        isCurrent ? "This is the current PowerFlow state; selecting a mode creates a manual lock until AUTO is restored." : "Select this mode to create a manual lock; AUTO releases the lock.",
        $"mode:{state}");

    private static string StateName(PowerState state) => state switch
    {
        PowerState.PowerSaver => "SAVER",
        PowerState.Balanced => "BALANCED",
        PowerState.HighPerformance => "PERFORMANCE",
        _ => state.ToString().ToUpperInvariant()
    };
}
