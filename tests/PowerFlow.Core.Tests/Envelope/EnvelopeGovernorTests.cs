using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.Core.Tests.Envelope;

public sealed class EnvelopeGovernorTests
{
    private static readonly OperatingEnvelope Envelope = new(20, 50, 80, 30, 50, 80);
    private static readonly DateTimeOffset T0 = new(2026, 9, 9, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_BrakesDemandAtAppCeiling()
    {
        var governor = new EnvelopeGovernor();
        var entitlement = new PerformanceEntitlement(EnvelopeZone.Efficient, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(10), true);

        var decision = governor.Evaluate(Observation(95, T0), Envelope, entitlement, T0);

        Assert.Equal(EnvelopeZone.Boost, decision.RequestedZone);
        Assert.Equal(EnvelopeZone.Efficient, decision.AllowedZone);
        Assert.Equal(EnvelopeDecisionKind.Brake, decision.Kind);
        Assert.Contains("ceiling", decision.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_QualifiesThenGrantsTemporaryBoostLease()
    {
        var governor = new EnvelopeGovernor();
        var entitlement = BoostEntitlement();

        var start = governor.Evaluate(Observation(95, T0), Envelope, entitlement, T0);
        var halfway = governor.Evaluate(Observation(95, T0.AddSeconds(2)), Envelope, entitlement, T0.AddSeconds(2));
        var granted = governor.Evaluate(Observation(95, T0.AddSeconds(4)), Envelope, entitlement, T0.AddSeconds(4));

        Assert.Equal(EnvelopeDecisionKind.Qualifying, start.Kind);
        Assert.Equal(0, start.QualificationProgress);
        Assert.Equal(EnvelopeDecisionKind.Qualifying, halfway.Kind);
        Assert.Equal(.5, halfway.QualificationProgress, 3);
        Assert.Equal(EnvelopeDecisionKind.Lease, granted.Kind);
        Assert.Equal(EnvelopeZone.Boost, granted.AllowedZone);
        Assert.Equal(T0.AddSeconds(16), granted.LeaseExpiresAt);
    }

    [Fact]
    public void Evaluate_RenewsLeaseWhileBoostPressureRemainsUseful()
    {
        var governor = new EnvelopeGovernor();
        var entitlement = BoostEntitlement();
        governor.Evaluate(Observation(95, T0), Envelope, entitlement, T0);
        var first = governor.Evaluate(Observation(95, T0.AddSeconds(4)), Envelope, entitlement, T0.AddSeconds(4));
        var renewed = governor.Evaluate(Observation(95, T0.AddSeconds(7)), Envelope, entitlement, T0.AddSeconds(7));

        Assert.Equal(EnvelopeDecisionKind.Lease, renewed.Kind);
        Assert.True(renewed.LeaseExpiresAt > first.LeaseExpiresAt);
        Assert.Equal(T0.AddSeconds(19), renewed.LeaseExpiresAt);
    }

    [Fact]
    public void Evaluate_UsesReleaseHysteresisBeforeClosingLease()
    {
        var governor = new EnvelopeGovernor();
        var entitlement = BoostEntitlement();
        governor.Evaluate(Observation(95, T0), Envelope, entitlement, T0);
        governor.Evaluate(Observation(95, T0.AddSeconds(4)), Envelope, entitlement, T0.AddSeconds(4));

        var holding = governor.Evaluate(Observation(10, T0.AddSeconds(5)), Envelope, entitlement, T0.AddSeconds(5));
        var released = governor.Evaluate(Observation(10, T0.AddSeconds(16)), Envelope, entitlement, T0.AddSeconds(16));

        Assert.Equal(EnvelopeDecisionKind.Lease, holding.Kind);
        Assert.Equal(EnvelopeZone.Boost, holding.AllowedZone);
        Assert.Equal(EnvelopeDecisionKind.None, released.Kind);
        Assert.Equal(EnvelopeZone.Eco, released.AllowedZone);
    }

    [Fact]
    public void Evaluate_ManualOverrideTakesPrecedence()
    {
        var governor = new EnvelopeGovernor();
        var decision = governor.Evaluate(Observation(95, T0), Envelope, BoostEntitlement(), T0, EnvelopeConfidence.High, EnvelopeZone.Eco);

        Assert.Equal(EnvelopeZone.Eco, decision.AllowedZone);
        Assert.Equal(EnvelopeDecisionKind.None, decision.Kind);
        Assert.Contains("override", decision.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_LowConfidenceCannotSilentlyGrantBoost()
    {
        var governor = new EnvelopeGovernor();
        var decision = governor.Evaluate(Observation(95, T0), Envelope, BoostEntitlement(), T0, EnvelopeConfidence.Low);

        Assert.Equal(EnvelopeZone.Boost, decision.RequestedZone);
        Assert.Equal(EnvelopeZone.Responsive, decision.AllowedZone);
        Assert.Equal(EnvelopeDecisionKind.Brake, decision.Kind);
        Assert.Contains("confidence", decision.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    private static PerformanceEntitlement BoostEntitlement() => new(EnvelopeZone.Boost, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(10), true);
    private static OperatingObservation Observation(double pressure, DateTimeOffset at) => new(at, pressure, 60, 2800, null, 16, EnvelopeZone.Efficient, "work.exe", EnvelopeDecisionKind.None);
}
