using PowerFlow.App.Dashboard;
using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ModelExplanationProjectionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 10, 17, 0, 0, TimeSpan.Zero);
    private static readonly OperatingEnvelope Envelope = new(18, 48, 78, 28, 52, 86);
    private static readonly PerformanceEntitlement Entitlement = new(EnvelopeZone.Responsive, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), true);

    [Fact]
    public void HighConfidence_CurrentEfficientRegion_ExplainsLearnedDoingAndConfidence()
    {
        var observation = new OperatingObservation(T0, 35, 42, 3200, 5, 16, EnvelopeZone.Efficient, "chrome.exe", EnvelopeDecisionKind.None);
        var decision = new GovernorDecision(EnvelopeZone.Efficient, EnvelopeZone.Efficient, EnvelopeDecisionKind.None, 0, null, "Holding in Efficient", EnvelopeConfidence.High);

        var result = ModelExplanationProjection.Create(Envelope, EnvelopeConfidence.High, observation, Entitlement, decision, manualMode: false);

        Assert.Contains("learned", result.WhatLearned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("efficient", result.WhatLearned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("chrome", result.WhatDoingNow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Efficient", result.WhatDoingNow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("high", result.ConfidenceExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.CurrentPoint);
        Assert.Equal(42, result.CurrentPoint!.PackageWatts);
        Assert.Equal(3200, result.CurrentPoint.EffectiveClockMhz);
        Assert.Equal(5, result.CurrentPoint.AwakeCores);
    }

    [Theory]
    [InlineData(EnvelopeConfidence.Low, "learning")]
    [InlineData(EnvelopeConfidence.Medium, "some")]
    [InlineData(EnvelopeConfidence.High, "enough")]
    public void Confidence_IsExplainedInPlainLanguage(EnvelopeConfidence confidence, string expected)
    {
        var result = ModelExplanationProjection.Create(Envelope, confidence, null, Entitlement, null, manualMode: false);
        Assert.Contains(expected, result.ConfidenceExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(EnvelopeDecisionKind.Brake, "holding")]
    [InlineData(EnvelopeDecisionKind.Qualifying, "wait")]
    [InlineData(EnvelopeDecisionKind.Lease, "temporary")]
    public void CurrentDecision_IsExplainedWithoutInternalVocabulary(EnvelopeDecisionKind kind, string expected)
    {
        var observation = new OperatingObservation(T0, 72, 78, 4300, 12, 16, EnvelopeZone.Responsive, "build.exe", kind);
        var decision = new GovernorDecision(EnvelopeZone.Boost, EnvelopeZone.Responsive, kind, .5, kind == EnvelopeDecisionKind.Lease ? T0.AddSeconds(8) : null, "internal explanation", EnvelopeConfidence.High);
        var result = ModelExplanationProjection.Create(Envelope, EnvelopeConfidence.High, observation, Entitlement, decision, manualMode: false);
        Assert.Contains(expected, result.WhatDoingNow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lease", result.WhatDoingNow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManualMode_IsExplicitAndNoObservationDoesNotInventEvidence()
    {
        var manual = ModelExplanationProjection.Create(Envelope, EnvelopeConfidence.Medium, null, Entitlement, null, manualMode: true);
        Assert.Contains("you", manual.WhatDoingNow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manual", manual.WhatDoingNow, StringComparison.OrdinalIgnoreCase);
        Assert.Null(manual.CurrentPoint);
        Assert.Contains("waiting", manual.WhatLearned, StringComparison.OrdinalIgnoreCase);
    }
}
