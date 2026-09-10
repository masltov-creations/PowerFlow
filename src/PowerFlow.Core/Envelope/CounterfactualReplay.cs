namespace PowerFlow.Core.Envelope;

public sealed record CounterfactualReplayResult(
    int ObservationCount,
    int ChangedDecisionCount,
    IReadOnlyDictionary<EnvelopeZone, int> OriginalResidency,
    IReadOnlyDictionary<EnvelopeZone, int> CandidateResidency,
    double? EstimatedEnergyDeltaWh,
    double? EstimatedPerformanceDeltaPercent);

public static class CounterfactualReplay
{
    public static CounterfactualReplayResult Run(
        IReadOnlyList<OperatingObservation> observations,
        OperatingEnvelope learnedEnvelope,
        PerformanceEntitlement learnedEntitlement,
        EnvelopeTuning tuning,
        EnvelopeConfidence confidence = EnvelopeConfidence.High)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(learnedEnvelope);
        ArgumentNullException.ThrowIfNull(learnedEntitlement);
        ArgumentNullException.ThrowIfNull(tuning);

        var candidateEnvelope = tuning.ApplyTo(learnedEnvelope);
        var candidateEntitlement = tuning.ApplyTo(learnedEntitlement);
        var originalResidency = EmptyResidency();
        var candidateResidency = EmptyResidency();
        var governor = new EnvelopeGovernor();
        var changed = 0;

        foreach (var observation in observations.OrderBy(o => o.At))
        {
            originalResidency[observation.Zone]++;
            var decision = governor.Evaluate(
                observation,
                candidateEnvelope,
                candidateEntitlement,
                observation.At,
                confidence,
                tuning.ManualOverrideZone);
            candidateResidency[decision.AllowedZone]++;
            if (decision.Kind != observation.Decision || decision.AllowedZone != observation.Zone)
                changed++;
        }

        return new CounterfactualReplayResult(
            observations.Count,
            changed,
            originalResidency,
            candidateResidency,
            EstimatedEnergyDeltaWh: null,
            EstimatedPerformanceDeltaPercent: null);
    }

    private static Dictionary<EnvelopeZone, int> EmptyResidency() =>
        Enum.GetValues<EnvelopeZone>().ToDictionary(zone => zone, _ => 0);
}