using PowerFlow.App.Dashboard;
using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class InspectionExplanationProjectionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 10, 17, 30, 12, TimeSpan.Zero);

    [Fact]
    public void ObservationInspection_ContainsEveryVisibleMetricActorPolicyAndPlainDecision()
    {
        var observation = new OperatingObservation(T0, 63.4, 71.2, 4175, 9, 16, EnvelopeZone.Responsive, @"C:\Apps\render.exe", EnvelopeDecisionKind.Qualifying);
        var result = InspectionExplanationProjection.ForObservation(observation);
        Assert.Contains("17:30:12", result.Title, StringComparison.Ordinal);
        Assert.Contains("63.4%", result.Metrics, StringComparison.Ordinal);
        Assert.Contains("71.2 W", result.Metrics, StringComparison.Ordinal);
        Assert.Contains("4.18 GHz", result.Metrics, StringComparison.Ordinal);
        Assert.Contains("9/16", result.Metrics, StringComparison.Ordinal);
        Assert.Contains("render", result.Context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Responsive", result.Context, StringComparison.Ordinal);
        Assert.Contains("waiting", result.Decision, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Qualifying", result.Decision, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingTruthfulMetrics_AreExplicitlyUnavailable()
    {
        var observation = new OperatingObservation(T0, 10, null, null, null, 16, EnvelopeZone.Eco, null, EnvelopeDecisionKind.None);
        var result = InspectionExplanationProjection.ForObservation(observation);
        Assert.Contains("power unavailable", result.Metrics, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clock unavailable", result.Metrics, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cores awake unavailable", result.Metrics, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no dominant app", result.Context, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(TimelinePolicyHandleKind.EcoPressure, "Eco")]
    [InlineData(TimelinePolicyHandleKind.EfficientPressure, "Efficient")]
    [InlineData(TimelinePolicyHandleKind.ResponsivePressure, "Responsive")]
    [InlineData(TimelinePolicyHandleKind.QualificationDuration, "stay")]
    [InlineData(TimelinePolicyHandleKind.LeaseDuration, "temporary")]
    [InlineData(TimelinePolicyHandleKind.ReleaseHysteresis, "quiet")]
    public void PolicyInspection_ExplainsWhatDraggingTheHandleChanges(TimelinePolicyHandleKind kind, string expected)
    {
        var result = InspectionExplanationProjection.ForPolicyHandle(kind, 42, TimeSpan.FromSeconds(3));
        Assert.Contains(expected, result.Decision, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drag", result.Context, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AtlasCellInspection_ExplainsResidencyActorsAndDecisionsWithoutChangingSelection()
    {
        var observations = new[]
        {
            new OperatingObservation(T0, 40, 45, 3100, 5, 16, EnvelopeZone.Efficient, "browser.exe", EnvelopeDecisionKind.None),
            new OperatingObservation(T0.AddSeconds(1), 42, 47, 3200, 6, 16, EnvelopeZone.Efficient, "browser.exe", EnvelopeDecisionKind.Brake),
        };
        var result = InspectionExplanationProjection.ForAtlasCell(observations, 0.75);
        Assert.Contains("75%", result.Metrics, StringComparison.Ordinal);
        Assert.Contains("browser", result.Context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("held", result.Decision, StringComparison.OrdinalIgnoreCase);
    }
}
