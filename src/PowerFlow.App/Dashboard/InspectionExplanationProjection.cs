using PowerFlow.Core.Envelope;

namespace PowerFlow.App.Dashboard;

public sealed record InspectionExplanation(
    string Title,
    string Metrics,
    string Context,
    string Decision)
{
    public string AsPlainText() => $"{Title}\n{Metrics}\n{Context}\n{Decision}";
}

public static class InspectionExplanationProjection
{
    public static InspectionExplanation ForObservation(OperatingObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var power = observation.PackageWatts is double watts ? $"{watts:0.0} W" : "power unavailable";
        var clock = observation.EffectiveClockMhz is double mhz ? $"{mhz / 1000d:0.00} GHz" : "clock unavailable";
        var cores = observation.ActiveCores is int awake
            ? observation.TotalCores is int total ? $"{awake}/{total} cores awake" : $"{awake} cores awake"
            : "cores awake unavailable";
        var actor = string.IsNullOrWhiteSpace(observation.Actor) ? "no dominant app identified" : ShortActor(observation.Actor!);
        return new InspectionExplanation(
            observation.At.ToString("HH:mm:ss"),
            $"CPU {observation.CpuPressurePercent:0.0}% · {power} · {clock} · {cores}",
            $"{actor} · {FriendlyZone(observation.Zone)} policy",
            ExplainDecision(observation.Decision, observation.Zone));
    }

    public static InspectionExplanation ForPolicyHandle(TimelinePolicyHandleKind kind, double value, TimeSpan duration)
    {
        var (title, metric, context, decision) = kind switch
        {
            TimelinePolicyHandleKind.EcoPressure => ("Eco threshold", $"CPU pressure {value:0.#}%", "Drag this horizontal rail to change the Eco boundary.", "Below this boundary PowerFlow can keep the machine in Eco."),
            TimelinePolicyHandleKind.EfficientPressure => ("Efficient threshold", $"CPU pressure {value:0.#}%", "Drag this horizontal rail to change the Efficient boundary.", "The Efficient boundary controls when PowerFlow can start considering Responsive behavior."),
            TimelinePolicyHandleKind.ResponsivePressure => ("Responsive threshold", $"CPU pressure {value:0.#}%", "Drag this horizontal rail to change the Responsive boundary.", "The Responsive boundary controls when sustained demand can start asking for Boost."),
            TimelinePolicyHandleKind.EcoPowerFrontier => ("Learned Eco power region", $"About {value:0.#} W", "This learned rail is evidence, not a manual power cap.", "PowerFlow uses it to understand where Eco operation has historically been efficient."),
            TimelinePolicyHandleKind.EfficientPowerFrontier => ("Learned efficient power region", $"About {value:0.#} W", "This learned rail is evidence, not a manual power cap.", "PowerFlow uses it to recognize when extra performance starts becoming more expensive."),
            TimelinePolicyHandleKind.ResponsivePowerFrontier => ("Learned responsive power region", $"About {value:0.#} W", "This learned rail is evidence, not a manual power cap.", "PowerFlow uses it as evidence about the cost of higher-performance operation."),
            TimelinePolicyHandleKind.QualificationDuration => ("Qualification time", $"{duration.TotalSeconds:0.#} seconds", "Drag this timing edge to change how long demand must persist.", "Demand must stay high for this long before PowerFlow opens a higher mode."),
            TimelinePolicyHandleKind.LeaseDuration => ("Temporary boost time", $"{duration.TotalSeconds:0.#} seconds", "Drag this timing edge to change the temporary performance window.", "A qualifying workload can receive temporary extra performance for this long before renewal is reconsidered."),
            TimelinePolicyHandleKind.ReleaseHysteresis => ("Release quiet time", $"{duration.TotalSeconds:0.#} seconds", "Drag this timing edge to change how long the machine must stay quiet.", "PowerFlow waits for this quiet period before stepping performance back down."),
            _ => ("Policy control", $"{value:0.#}", "Drag this control to tune the selected policy.", "PowerFlow will preview the change before you save it.")
        };
        return new InspectionExplanation(title, metric, context, decision);
    }

    public static InspectionExplanation ForAtlasCell(IReadOnlyList<OperatingObservation> observations, double density)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0)
            return new InspectionExplanation("Operating region", "time spent here unavailable", "No observations in this region", "There is not enough evidence here to explain a decision.");

        var ordered = observations.OrderBy(o => o.At).ToArray();
        var actors = ordered.Select(o => string.IsNullOrWhiteSpace(o.Actor) ? null : ShortActor(o.Actor!)).Where(a => a is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var actorText = actors.Length switch { 0 => "no dominant app identified", 1 => actors[0]!, _ => $"{actors[0]} + {actors.Length - 1} others" };
        var decisions = ordered.Select(o => o.Decision).Where(d => d != EnvelopeDecisionKind.None).ToArray();
        var decision = decisions.Contains(EnvelopeDecisionKind.Brake)
            ? "At least one workload was held below the performance level it requested in this region."
            : decisions.Contains(EnvelopeDecisionKind.Qualifying)
                ? "At least one workload was waiting to prove that higher demand would persist in this region."
                : decisions.Contains(EnvelopeDecisionKind.Lease)
                    ? "At least one workload had temporary extra performance while the machine was in this region."
                    : "PowerFlow did not need to intervene while these observations were in this region.";
        return new InspectionExplanation(
            ordered.Length == 1 ? ordered[0].At.ToString("HH:mm:ss") : $"{ordered[0].At:HH:mm:ss}–{ordered[^1].At:HH:mm:ss}",
            $"{Math.Clamp(density, 0, 1):P0} relative residency · {ordered.Length} observation{(ordered.Length == 1 ? string.Empty : "s")}",
            $"{actorText} · {FriendlyZone(ordered.GroupBy(o => o.Zone).OrderByDescending(g => g.Count()).First().Key)} most common policy",
            decision);
    }

    private static string ExplainDecision(EnvelopeDecisionKind decision, EnvelopeZone zone) => decision switch
    {
        EnvelopeDecisionKind.Brake => $"PowerFlow held the workload in {FriendlyZone(zone)} instead of allowing a higher mode.",
        EnvelopeDecisionKind.Qualifying => "PowerFlow is waiting for demand to stay high long enough before allowing more performance.",
        EnvelopeDecisionKind.Lease => $"PowerFlow granted temporary extra performance in {FriendlyZone(zone)} and will reevaluate it.",
        _ => $"PowerFlow allowed {FriendlyZone(zone)} without an additional intervention."
    };

    private static string FriendlyZone(EnvelopeZone zone) => zone switch
    {
        EnvelopeZone.Eco => "Eco",
        EnvelopeZone.Efficient => "Efficient",
        EnvelopeZone.Responsive => "Responsive",
        EnvelopeZone.Boost => "Boost",
        _ => zone.ToString()
    };

    private static string ShortActor(string actor)
    {
        try { return Path.GetFileNameWithoutExtension(actor); }
        catch { return actor; }
    }
}
