using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.Core.Tests.Envelope;

public sealed class CounterfactualReplayTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 9, 20, 0, 0, TimeSpan.Zero);
    private static readonly OperatingEnvelope Learned = new(20, 50, 80, 25, 45, 75);
    private static readonly PerformanceEntitlement DefaultEntitlement = new(
        EnvelopeZone.Boost,
        TimeSpan.Zero,
        TimeSpan.FromSeconds(12),
        TimeSpan.FromSeconds(6),
        true);

    [Fact]
    public void Replay_WithLearnedTuning_IsDeterministicAndPreservesResidency()
    {
        var history = new[]
        {
            Obs(0, 10, EnvelopeZone.Eco),
            Obs(1, 40, EnvelopeZone.Efficient),
            Obs(2, 70, EnvelopeZone.Responsive),
        };
        var tuning = EnvelopeTuning.Learned;

        var first = CounterfactualReplay.Run(history, Learned, DefaultEntitlement, tuning);
        var second = CounterfactualReplay.Run(history, Learned, DefaultEntitlement, tuning);

        Assert.Equal(0, first.ChangedDecisionCount);
        Assert.Equal(first.ChangedDecisionCount, second.ChangedDecisionCount);
        Assert.Equal(first.ObservationCount, second.ObservationCount);
        foreach (var zone in Enum.GetValues<EnvelopeZone>())
        {
            Assert.Equal(first.OriginalResidency[zone], second.OriginalResidency[zone]);
            Assert.Equal(first.CandidateResidency[zone], second.CandidateResidency[zone]);
            Assert.Equal(first.OriginalResidency[zone], first.CandidateResidency[zone]);
        }
    }

    [Fact]
    public void Replay_DoesNotMutateRawOperatingHistory()
    {
        var history = new[]
        {
            Obs(0, 55, EnvelopeZone.Responsive),
            Obs(1, 88, EnvelopeZone.Boost),
        };
        var before = history.Select(o => o with { }).ToArray();
        var tuning = new EnvelopeTuning(EnvelopeTuningLayer.Tuned, efficientCeilingPressure: 65);

        _ = CounterfactualReplay.Run(history, Learned, DefaultEntitlement, tuning);

        Assert.Equal(before, history);
    }

    [Fact]
    public void Replay_BoundaryTuningChangesOnlyAffectedDecisionResidency()
    {
        var history = new[]
        {
            Obs(0, 45, EnvelopeZone.Efficient),
            Obs(1, 55, EnvelopeZone.Responsive),
            Obs(2, 75, EnvelopeZone.Responsive),
        };
        var tuning = new EnvelopeTuning(EnvelopeTuningLayer.Tuned, efficientCeilingPressure: 60);

        var result = CounterfactualReplay.Run(history, Learned, DefaultEntitlement, tuning);

        Assert.Equal(3, result.ObservationCount);
        Assert.Equal(1, result.ChangedDecisionCount);
        Assert.Equal(1, result.OriginalResidency[EnvelopeZone.Efficient]);
        Assert.Equal(2, result.CandidateResidency[EnvelopeZone.Efficient]);
        Assert.Equal(2, result.OriginalResidency[EnvelopeZone.Responsive]);
        Assert.Equal(1, result.CandidateResidency[EnvelopeZone.Responsive]);
    }

    [Fact]
    public void Replay_LeavesEnergyAndPerformanceEstimatesNullWithoutMeasuredEvidence()
    {
        var history = new[] { Obs(0, 55, EnvelopeZone.Responsive) };
        var result = CounterfactualReplay.Run(history, Learned, DefaultEntitlement,
            new EnvelopeTuning(EnvelopeTuningLayer.Tuned, efficientCeilingPressure: 60));

        Assert.Null(result.EstimatedEnergyDeltaWh);
        Assert.Null(result.EstimatedPerformanceDeltaPercent);
    }

    [Fact]
    public void Tuning_AppliesLeaseAndEntitlementSemanticsWithoutRawActuatorValues()
    {
        var tuning = new EnvelopeTuning(
            EnvelopeTuningLayer.Tuned,
            maximumZone: EnvelopeZone.Responsive,
            qualificationDuration: TimeSpan.FromSeconds(2),
            leaseDuration: TimeSpan.FromSeconds(8),
            releaseHysteresis: TimeSpan.FromSeconds(3));

        var entitlement = tuning.ApplyTo(DefaultEntitlement);

        Assert.Equal(EnvelopeZone.Responsive, entitlement.MaximumZone);
        Assert.Equal(TimeSpan.FromSeconds(2), entitlement.QualificationDuration);
        Assert.Equal(TimeSpan.FromSeconds(8), entitlement.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), entitlement.ReleaseHysteresis);
        Assert.Equal(EnvelopeTuningLayer.Tuned, tuning.Layer);
    }

    [Fact]
    public void Tuning_OverrideCanHoldASemanticZoneForReplay()
    {
        var history = new[] { Obs(0, 90, EnvelopeZone.Boost) };
        var tuning = new EnvelopeTuning(EnvelopeTuningLayer.Override, manualOverrideZone: EnvelopeZone.Efficient);

        var result = CounterfactualReplay.Run(history, Learned, DefaultEntitlement, tuning);

        Assert.Equal(1, result.ChangedDecisionCount);
        Assert.Equal(1, result.CandidateResidency[EnvelopeZone.Efficient]);
        Assert.Equal(0, result.CandidateResidency[EnvelopeZone.Boost]);
    }

    [Fact]
    public void Tuning_InvalidBoundaryOrderingIsRejectedWhenApplied()
    {
        var tuning = new EnvelopeTuning(EnvelopeTuningLayer.Tuned, ecoCeilingPressure: 70, efficientCeilingPressure: 60);
        Assert.Throws<ArgumentOutOfRangeException>(() => tuning.ApplyTo(Learned));
    }

    private static OperatingObservation Obs(int second, double pressure, EnvelopeZone zone) =>
        new(T0.AddSeconds(second), pressure, 40 + second, 3000 + second * 10, 4, 8, zone, "work.exe", EnvelopeDecisionKind.None);
}