using PowerFlow.Core.Envelope;

namespace PowerFlow.App.Dashboard;

public sealed record ModelCurrentPoint(
    double CpuPressurePercent,
    double? PackageWatts,
    double? EffectiveClockMhz,
    int? AwakeCores,
    int? TotalCores,
    string? Actor = null,
    EnvelopeZone? Zone = null);

public sealed record ModelExplanation(
    string WhatLearned,
    string WhatDoingNow,
    string ConfidenceExplanation,
    ModelCurrentPoint? CurrentPoint);

public static class ModelExplanationProjection
{
    public static ModelExplanation Create(
        OperatingEnvelope envelope,
        EnvelopeConfidence confidence,
        OperatingObservation? current,
        PerformanceEntitlement entitlement,
        GovernorDecision? decision,
        bool manualMode)
        => Build(
            envelope,
            confidence,
            current,
            entitlement,
            decision,
            manualMode,
            current?.Zone switch
            {
                EnvelopeZone.Eco => PowerModeSelection.Eco,
                EnvelopeZone.Efficient => PowerModeSelection.Efficient,
                EnvelopeZone.Responsive => PowerModeSelection.Responsive,
                EnvelopeZone.Boost => PowerModeSelection.Boost,
                _ => PowerModeSelection.Auto
            });

    public static ModelExplanation Build(
        OperatingEnvelope learnedEnvelope,
        EnvelopeConfidence confidence,
        OperatingObservation? latest,
        PerformanceEntitlement entitlement,
        GovernorDecision? decision,
        bool manualAuthority,
        PowerModeSelection manualMode)
    {
        ArgumentNullException.ThrowIfNull(learnedEnvelope);
        ArgumentNullException.ThrowIfNull(entitlement);

        var learned = ExplainLearning(learnedEnvelope, confidence, latest);
        var confidenceText = confidence switch
        {
            EnvelopeConfidence.Low => "PowerFlow is still learning this machine. These boundaries can move as more real workloads are observed.",
            EnvelopeConfidence.Medium => "PowerFlow has some useful evidence for this model, but it is still adapting the boundaries as more workloads are observed.",
            EnvelopeConfidence.High => "PowerFlow has enough repeated operating evidence to use these boundaries with high confidence. It will keep learning as workloads change.",
            _ => "PowerFlow is evaluating how reliable the available evidence is."
        };

        if (latest is null)
        {
            return new ModelExplanation(
                learned,
                manualAuthority
                    ? "You are in manual control. PowerFlow will hold your selected mode until you return to Auto."
                    : "PowerFlow is observing the machine and waiting for a current operating sample.",
                confidenceText,
                null);
        }

        var currentPoint = new ModelCurrentPoint(
            latest.CpuPressurePercent,
            latest.PackageWatts,
            latest.EffectiveClockMhz,
            latest.ActiveCores,
            latest.TotalCores,
            latest.Actor,
            latest.Zone);

        if (manualAuthority)
        {
            return new ModelExplanation(
                learned,
                $"You are in manual control at {FriendlyMode(manualMode)}. PowerFlow is still observing the workload, but it will not override your selection until you return to Auto.",
                confidenceText,
                currentPoint);
        }

        return new ModelExplanation(
            learned,
            ExplainDecision(learnedEnvelope, latest, entitlement, decision),
            confidenceText,
            currentPoint);
    }

    private static string ExplainLearning(OperatingEnvelope envelope, EnvelopeConfidence confidence, OperatingObservation? latest)
    {
        if (latest is null)
            return "Waiting for enough live operating history to explain what PowerFlow has learned about this machine.";

        if (envelope.EfficientPowerFrontierWatts is double frontier && double.IsFinite(frontier))
        {
            var relation = latest.PackageWatts is double currentWatts && currentWatts > frontier * 1.15
                ? "The machine is currently above that learned efficient region."
                : "The machine is currently inside or near that learned efficient region.";
            return $"PowerFlow learned an efficient sustained-work region near or below {frontier:0.#} W, with CPU-pressure boundaries around {envelope.EcoCeilingPressure:0.#}%, {envelope.EfficientCeilingPressure:0.#}%, and {envelope.ResponsiveCeilingPressure:0.#}%. {relation}";
        }

        return confidence == EnvelopeConfidence.Low
            ? "PowerFlow is still learning where this machine is efficient and where extra performance starts costing disproportionately more power."
            : $"PowerFlow learned CPU-pressure boundaries around {envelope.EcoCeilingPressure:0.#}%, {envelope.EfficientCeilingPressure:0.#}%, and {envelope.ResponsiveCeilingPressure:0.#}%. Package-power evidence is not strong enough yet for a reliable efficiency frontier.";
    }

    private static string ExplainDecision(
        OperatingEnvelope envelope,
        OperatingObservation latest,
        PerformanceEntitlement entitlement,
        GovernorDecision? decision)
    {
        var actor = string.IsNullOrWhiteSpace(latest.Actor) ? "The current workload" : ShortActor(latest.Actor!);
        var currentRegion = CurrentRegion(envelope, latest);
        if (decision is null)
            return $"{actor} is currently {currentRegion}. PowerFlow is observing demand against its learned policy.";

        var allowed = FriendlyZone(decision.AllowedZone);
        var ceiling = FriendlyZone(entitlement.MaximumZone);
        return decision.Kind switch
        {
            EnvelopeDecisionKind.Brake => $"{actor} is asking for more performance, but PowerFlow is holding it at {allowed}. The current policy ceiling is {ceiling}; the machine is {currentRegion}.",
            EnvelopeDecisionKind.Qualifying => $"{actor} may benefit from more performance. PowerFlow is waiting for demand to stay high long enough before opening {FriendlyZone(decision.RequestedZone)}; qualification is {Math.Clamp(decision.QualificationProgress, 0, 1):P0} complete.",
            EnvelopeDecisionKind.Lease => $"{actor} has temporary access to {allowed}. PowerFlow will keep checking whether that extra performance is still justified{LeaseSuffix(decision.LeaseExpiresAt, latest.At)}.",
            _ => $"{actor} is currently operating in {allowed}, within its policy ceiling of {ceiling}. The machine is {currentRegion}."
        };
    }

    private static string CurrentRegion(OperatingEnvelope envelope, OperatingObservation latest)
    {
        if (latest.PackageWatts is double watts && envelope.EfficientPowerFrontierWatts is double efficientFrontier)
            return watts <= efficientFrontier
                ? $"in the learned efficient region at {watts:0.#} W"
                : $"above the learned efficient power region at {watts:0.#} W";
        return $"in the {FriendlyZone(envelope.ZoneForPressure(latest.CpuPressurePercent))} pressure region at {latest.CpuPressurePercent:0.#}% CPU pressure";
    }

    private static string LeaseSuffix(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt is not DateTimeOffset expiry) return string.Empty;
        var seconds = Math.Max(0, (expiry - now).TotalSeconds);
        return $", with about {seconds:0.#} seconds remaining";
    }

    private static string FriendlyZone(EnvelopeZone zone) => zone switch
    {
        EnvelopeZone.Eco => "Eco",
        EnvelopeZone.Efficient => "Efficient",
        EnvelopeZone.Responsive => "Responsive",
        EnvelopeZone.Boost => "Boost",
        _ => zone.ToString()
    };

    private static string FriendlyMode(PowerModeSelection mode) => mode switch
    {
        PowerModeSelection.Eco => "Eco",
        PowerModeSelection.Efficient => "Efficient",
        PowerModeSelection.Responsive => "Responsive",
        PowerModeSelection.Boost => "Boost",
        _ => "Auto"
    };

    private static string ShortActor(string actor)
    {
        try { return Path.GetFileNameWithoutExtension(actor); }
        catch { return actor; }
    }
}
