using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;

namespace PowerFlow.App.Controller;

public sealed record EnvelopeActuationResult(
    bool Eligible,
    PowerState? TargetState,
    EnvelopeZone EffectiveZone,
    string Reason);

public static class EnvelopeActuationPolicy
{
    public static EnvelopeActuationResult Evaluate(
        bool adaptiveActuationEnabled,
        bool autoEnabled,
        EnvelopeConfidence confidence,
        bool manualLatched,
        bool gameLatched,
        GovernorDecision governorDecision,
        PerformanceEntitlement entitlement)
    {
        ArgumentNullException.ThrowIfNull(governorDecision);
        ArgumentNullException.ThrowIfNull(entitlement);

        var effectiveZone = governorDecision.AllowedZone > entitlement.MaximumZone
            ? entitlement.MaximumZone
            : governorDecision.AllowedZone;

        if (!adaptiveActuationEnabled)
            return Block(effectiveZone, "Adaptive governor remains advisory until adaptive actuation is explicitly enabled.");
        if (!autoEnabled)
            return Block(effectiveZone, "Adaptive actuation is paused because Auto is not enabled.");
        if (manualLatched || gameLatched)
            return Block(effectiveZone, "An explicit manual/game latch takes precedence over adaptive actuation.");
        if (confidence == EnvelopeConfidence.Low)
            return Block(effectiveZone, "Model confidence is low; adaptive actuation remains advisory.");
        if (confidence == EnvelopeConfidence.Medium && effectiveZone == EnvelopeZone.Boost)
            return Block(effectiveZone, "High confidence is required before adaptive actuation can enter Boost.");

        var target = MapZone(effectiveZone);
        var clamped = effectiveZone != governorDecision.AllowedZone;
        var reason = clamped
            ? $"App entitlement ceiling clamps {governorDecision.AllowedZone} to {effectiveZone}; map to {target}."
            : $"Qualified {effectiveZone} maps conservatively to {target}.";
        return new EnvelopeActuationResult(true, target, effectiveZone, reason);
    }

    public static PowerState MapZone(EnvelopeZone zone) => zone switch
    {
        EnvelopeZone.Eco => PowerState.PowerSaver,
        EnvelopeZone.Efficient => PowerState.Balanced,
        EnvelopeZone.Responsive => PowerState.Balanced,
        EnvelopeZone.Boost => PowerState.HighPerformance,
        _ => PowerState.Balanced
    };

    private static EnvelopeActuationResult Block(EnvelopeZone zone, string reason)
        => new(false, null, zone, reason);
}
