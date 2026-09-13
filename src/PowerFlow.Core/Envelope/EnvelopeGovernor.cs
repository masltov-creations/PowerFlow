namespace PowerFlow.Core.Envelope;

public sealed class EnvelopeGovernor
{
    private DateTimeOffset? _qualificationSince;
    private BoostLease? _lease;
    private DateTimeOffset? _releaseSince;
    private EnvelopeZone? _heldZone;
    private DateTimeOffset? _downshiftSince;

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

        if (_heldZone is EnvelopeZone heldAboveCeiling && heldAboveCeiling > ceiling)
        {
            _heldZone = ceiling;
            _downshiftSince = null;
        }
        if (_lease is not null && ceiling < EnvelopeZone.Boost)
        {
            _lease = null;
            _releaseSince = null;
            _qualificationSince = null;
        }

        if (requested > ceiling)
        {
            _qualificationSince = null;
            _lease = null;
            _releaseSince = null;
            _heldZone = ceiling;
            _downshiftSince = null;
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
                _downshiftSince = null;
                _heldZone = EnvelopeZone.Boost;
                _lease = _lease with { ExpiresAt = now + entitlement.LeaseDuration };
                return Decision(requested, EnvelopeZone.Boost, EnvelopeDecisionKind.Lease, 1, _lease.ExpiresAt, "Boost lease renewed while qualified pressure remains.", confidence);
            }

            _releaseSince ??= now;
            if (now - _releaseSince.Value < entitlement.ReleaseHysteresis && now < _lease.ExpiresAt + entitlement.ReleaseHysteresis)
                return Decision(requested, EnvelopeZone.Boost, EnvelopeDecisionKind.Lease, 1, _lease.ExpiresAt, "Boost lease is draining through release hysteresis.", confidence);

            _lease = null;
            _releaseSince = null;
            _qualificationSince = null;
            _heldZone = requested;
            _downshiftSince = null;
            return Decision(requested, requested, EnvelopeDecisionKind.None, 0, null, $"Boost lease released after hysteresis; demand now fits {requested}.", confidence);
        }

        if (requested != EnvelopeZone.Boost)
        {
            _qualificationSince = null;
            return EvaluateNonBoostZone(requested, entitlement, now, confidence);
        }

        _downshiftSince = null;
        _qualificationSince ??= now;
        var duration = entitlement.QualificationDuration;
        var progress = duration <= TimeSpan.Zero ? 1 : Math.Clamp((now - _qualificationSince.Value).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
        if (progress < 1)
        {
            _heldZone = EnvelopeZone.Responsive;
            return Decision(requested, EnvelopeZone.Responsive, EnvelopeDecisionKind.Qualifying, progress, null, "Boost pressure is qualifying for a temporary lease.", confidence);
        }

        _lease = new BoostLease(now, now + entitlement.LeaseDuration, observation.Actor);
        _releaseSince = null;
        _heldZone = EnvelopeZone.Boost;
        return Decision(requested, EnvelopeZone.Boost, EnvelopeDecisionKind.Lease, 1, _lease.ExpiresAt, "Qualified pressure earned a temporary boost lease.", confidence);
    }

    private GovernorDecision EvaluateNonBoostZone(
        EnvelopeZone requested,
        PerformanceEntitlement entitlement,
        DateTimeOffset now,
        EnvelopeConfidence confidence)
    {
        if (_heldZone is not EnvelopeZone held || held == EnvelopeZone.Boost || requested > held)
        {
            _heldZone = requested;
            _downshiftSince = null;
            return Decision(requested, requested, EnvelopeDecisionKind.None, 0, null, $"Demand remains within {requested}.", confidence);
        }

        if (requested == held)
        {
            _downshiftSince = null;
            return Decision(requested, requested, EnvelopeDecisionKind.None, 0, null, $"Demand remains within {requested}.", confidence);
        }

        if (entitlement.ReleaseHysteresis <= TimeSpan.Zero)
        {
            _heldZone = requested;
            _downshiftSince = null;
            return Decision(requested, requested, EnvelopeDecisionKind.None, 0, null, $"Demand settled into {requested}.", confidence);
        }

        _downshiftSince ??= now;
        var elapsed = now - _downshiftSince.Value;
        if (elapsed < entitlement.ReleaseHysteresis)
        {
            var remaining = entitlement.ReleaseHysteresis - elapsed;
            return Decision(
                requested,
                held,
                EnvelopeDecisionKind.None,
                0,
                null,
                $"Recent {held} demand is held through release hysteresis while {requested} persists ({remaining.TotalSeconds:0.#}s remaining).",
                confidence);
        }

        _heldZone = requested;
        _downshiftSince = null;
        return Decision(requested, requested, EnvelopeDecisionKind.None, 0, null, $"Lower demand persisted through release hysteresis; settle into {requested}.", confidence);
    }

    private void ResetTransientState()
    {
        _qualificationSince = null;
        _lease = null;
        _releaseSince = null;
        _heldZone = null;
        _downshiftSince = null;
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