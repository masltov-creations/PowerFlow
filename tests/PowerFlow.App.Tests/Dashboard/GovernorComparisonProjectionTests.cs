using PowerFlow.App.Controller;
using PowerFlow.App.Dashboard;
using PowerFlow.Core.Envelope;
using PowerFlow.Core.Profiling;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class GovernorComparisonProjectionTests
{
    [Fact]
    public void Create_DistinguishesAppliedAutoFromShadowWouldUse()
    {
        var current = Decision(EnvelopeZone.Efficient);
        var shadow = Shadow(75, EnvelopeZone.Responsive, PowerFlowOperatingMode.BalancedPerformance);

        var view = GovernorComparisonProjection.Create(current, PowerFlowOperatingMode.Balanced, manualAuthority: false, shadow, baseline: null);

        Assert.Equal("APPLIED · AUTO / BAL-E", view.CurrentStateText);
        Assert.Contains("MODEL EFFICIENT", view.CurrentDetailText, StringComparison.Ordinal);
        Assert.Equal("SHADOW · T75", view.ShadowStateText);
        Assert.Contains("WOULD USE BAL-P", view.ShadowDetailText, StringComparison.Ordinal);
        Assert.Contains("RESPONSIVE", view.ShadowDetailText, StringComparison.Ordinal);
        Assert.Contains("ESTIMATE", view.ShadowEvidenceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ReportsManualAppliedAuthorityWithoutPretendingShadowApplied()
    {
        var view = GovernorComparisonProjection.Create(
            Decision(EnvelopeZone.Boost),
            PowerFlowOperatingMode.Performance,
            manualAuthority: true,
            Shadow(90, EnvelopeZone.Boost, PowerFlowOperatingMode.Performance),
            baseline: null);

        Assert.Equal("APPLIED · MANUAL / PERF", view.CurrentStateText);
        Assert.StartsWith("SHADOW", view.ShadowStateText, StringComparison.Ordinal);
        Assert.DoesNotContain("APPLIED", view.ShadowDetailText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "SAVER BIAS", "Scales later", "shorter sustain", "settles sooner")]
    [InlineData(50, "BAL-P", "Near current", "current sustain", "current settle")]
    [InlineData(100, "PERF BIAS", "Scales sooner", "longer sustain", "settles later")]
    public void Create_ExplainsBehaviorInOutcomeLanguage(double tension, string reference, string scale, string sustain, string settle)
    {
        var view = GovernorComparisonProjection.Create(
            Decision(EnvelopeZone.Efficient),
            PowerFlowOperatingMode.Balanced,
            false,
            Shadow(tension, EnvelopeZone.Efficient, PowerFlowOperatingMode.Balanced),
            baseline: null);

        Assert.Equal(reference, view.ReferenceText);
        Assert.Contains(scale, view.ScaleBehaviorText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(sustain, view.SustainBehaviorText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(settle, view.SettleBehaviorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_UsesOnlyMeasuredBaselineFactsForCorrespondingFixedProfiles()
    {
        var baseline = Baseline(
            Result(MachineBaselineMode.BalancedEfficient, "BAL-E", 64.2, 7092),
            Result(MachineBaselineMode.BalancedPerformance, "BAL-P", 76.4, 7087));
        var view = GovernorComparisonProjection.Create(
            Decision(EnvelopeZone.Efficient),
            PowerFlowOperatingMode.Balanced,
            false,
            Shadow(75, EnvelopeZone.Responsive, PowerFlowOperatingMode.BalancedPerformance),
            baseline);

        Assert.Equal("MEASURED · 64.2 W idle · 7092 Mops max", view.CurrentEvidenceText);
        Assert.Equal("MEASURED · 76.4 W idle · 7087 Mops max", view.ShadowEvidenceText);
        Assert.DoesNotContain("estimate", view.ShadowEvidenceText, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Create_SameAppliedAndShadowProfileDoesNotRepeatIdenticalMeasuredEvidence()
    {
        var baseline = Baseline(Result(MachineBaselineMode.BalancedEfficient, "BAL-E", 77.8, 7210));
        var view = GovernorComparisonProjection.Create(
            Decision(EnvelopeZone.Efficient),
            PowerFlowOperatingMode.Balanced,
            false,
            Shadow(50, EnvelopeZone.Efficient, PowerFlowOperatingMode.Balanced),
            baseline);

        Assert.Equal("MEASURED · 77.8 W idle · 7210 Mops max", view.CurrentEvidenceText);
        Assert.NotEqual(view.CurrentEvidenceText, view.ShadowEvidenceText);
        Assert.Contains("SAME PROFILE", view.ShadowEvidenceText, StringComparison.OrdinalIgnoreCase);
    }    private static GovernorDecision Decision(EnvelopeZone zone) =>
        new(zone, zone, EnvelopeDecisionKind.None, 0, null, "test", EnvelopeConfidence.High);

    private static TensionShadowEvaluation Shadow(double tension, EnvelopeZone zone, PowerFlowOperatingMode mode)
    {
        var envelope = new OperatingEnvelope(20, 50, 80, 35, 65, 95);
        var entitlement = new PerformanceEntitlement(EnvelopeZone.Boost, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(10), true);
        var reference = GovernorTensionPolicy.Resolve(tension, envelope, entitlement).Reference;
        return new TensionShadowEvaluation(tension, reference, Decision(zone), envelope, envelope, entitlement, EnvelopeConfidence.High, null, mode);
    }

    private static MachineBaselineModeResult Result(MachineBaselineMode mode, string label, double idleWatts, double throughput)
    {
        var at = new DateTimeOffset(2026, 9, 11, 20, 0, 0, TimeSpan.Zero);
        var idle = new MachineIdleSummary(TimeSpan.FromSeconds(90), idleWatts, idleWatts, 100, 4.2, 20, 0, 1, 90);
        var profile = CpuCapabilityAnalysis.Build(at, label, label, [new CpuCapabilityPoint(16, throughput, idleWatts, 100, 4.2)]);
        return new MachineBaselineModeResult(mode, label, label, idle, profile);
    }

    private static MachineBaselineComparisonRun Baseline(params MachineBaselineModeResult[] results)
    {
        var mode = results[0].Mode;
        return new MachineBaselineComparisonRun(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 11, 20, 0, 0, TimeSpan.Zero),
            MachineBaselineSchedule.Standard,
            results,
            new MachineBaselineRecommendation(mode, mode, mode, mode, mode, null, "test"));
    }
}
