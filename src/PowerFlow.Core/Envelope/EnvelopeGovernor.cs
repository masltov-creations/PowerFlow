namespace PowerFlow.Core.Envelope;

public sealed class EnvelopeGovernor
{
    private DateTimeOffset? _qualificationSince;
    private BoostLease? _lease;
    private DateTimeOffset? _releaseSince;

    public GovernorDecision Evaluate(
        OperatingObservation observation,
        OperatingEnvelope envelope,
        PerformanceEntitlement entitlement,
        DateTimeOffset now) => Evaluate(observation, envelope, entitlement, now, EnvelopeConfidence.High, null);

    public GovernorDecision Evaluate(
        OperatingObservation observation,
        OperatingEnvelope envelope,
        PerformanceEntitlement entitlement,
        DateTimeOffset now,
        EnvelopeConfidence confidence,
        EnvelopeZone? manualOverrideZone = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(entitlement);

        var requested = envelope.ZoneForPressure(observation.CpuPressurePercent);

        if (manualOverrideZone is EnvelopeZone manual)
        {
            ResetTransientState();
            return Decision(requested, manual, EnvelopeDecisionKind.None, 0, null, $"Manual override holds {manual}.", confidence);
        }

        var ceiling = entitlement.MaximumZone;
        if (confidence == EnvelopeConfidence.Low && ceiling > EnvelopeZone.Responsive)
            ceiling = EnvelopeZone.Responsive;

        if (requested > ceiling)
        {
            ResetTransientState();
            var reason = confidence == EnvelopeConfidence.Low && entitlement.MaximumZone > EnvelopeZone.Responsive
                ? "Boost withheld because model confidence is low."
                : $"Demand exceeds app entitlement ceiling {ceiling}.";
            return Decision(requested, ceiling, EnvelopeDecisionKind.Brake, 0, null, reason, confidence);
        }

        if (_lease is not null)
        {
            if (requested == EnvelopeZone.Boost)
            {
                _releaseSince = null;
                _lease = _lease with { ExpiresAt = now + entitlement.LeaseDuration };
                return Decision(requested, EnvelopeZone.Boost, EnvelopeDecisionKind.Lease, 1, _lease.ExpiresAt, "Boost lease renewed while qualified pressure remains.", confidence);
            }

            _releaseSince ??= now;
            if (now - _releaseSince.Value < entitlement.ReleaseHysteresis && now < _lease.ExpiresAt + entitlement.ReleaseHysteresis)
                return Decision(requested, EnvelopeZone.Boost, EnvelopeDecisionKind.Lease, 1, _lease.ExpiresAt, "Boost lease is draining through release hysteresis.", confidence);

            _lease = null;
            _releaseSince = null;
            _qualificationSince = null;
        }

        if (requested != EnvelopeZone.Boost)
        {
            _qualificationSince = null;
            return Decision(requested, requested, EnvelopeDecisionKind.None, 0, null, $"Demand remains within {requested}.", confidence);
        }

        _qualificationSince ??= now;
        var duration = entitlement.QualificationDuration;
        var progress = duration <= TimeSpan.Zero ? 1 : Math.Clamp((now - _qualificationSince.Value).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
        if (progress < 1)
            return Decision(requested, EnvelopeZone.Responsive, EnvelopeDecisionKind.Qualifying, progress, null, "Boost pressure is qualifying for a temporary lease.", confidence);

        _lease = new BoostLease(now, now + entitlement.LeaseDuration, observation.Actor);
        _releaseSince = null;
        return Decision(requested, EnvelopeZone.Boost, EnvelopeDecisionKind.Lease, 1, _lease.ExpiresAt, "Qualified pressure earned a temporary boost lease.", confidence);
    }

    private void ResetTransientState()
    {
        _qualificationSince = null;
        _lease = null;
        _releaseSince = null;
    }

    private static GovernorDecision Decision(
        EnvelopeZone requested,
        EnvelopeZone allowed,
        EnvelopeDecisionKind kind,
        double progress,
        DateTimeOffset? expires,
        string explanation,
        EnvelopeConfidence confidence) =>
        new(requested, allowed, kind, Math.Clamp(progress, 0, 1), expires, explanation, confidence);
}
