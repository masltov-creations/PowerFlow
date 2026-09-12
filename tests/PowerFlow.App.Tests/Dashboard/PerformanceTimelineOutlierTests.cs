using PowerFlow.App.Dashboard;
using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PerformanceTimelineOutlierTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 11, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_IsolatedPowerSpike_DoesNotFlattenDomainOrDrawHairline()
    {
        var history = new[]
        {
            Observation(0, 24, 82, 126),
            Observation(1, 25, 84, 127),
            Observation(2, 26, 850, 128),
            Observation(3, 27, 83, 129),
            Observation(4, 28, 86, 130),
        };

        var data = PerformanceTimelineProjection.Build(history, 4);
        var power = Assert.Single(data.Lanes, lane => lane.Metric == PerformanceTimelineMetric.PackagePower);

        Assert.True(power.DomainMax <= 140, $"Unexpected outlier-expanded domain {power.DomainMin:0.#}-{power.DomainMax:0.#}W");
        Assert.Null(power.Points[2].Y);
        Assert.NotNull(power.Points[1].Y);
        Assert.NotNull(power.Points[3].Y);
    }

    [Fact]
    public void Build_SustainedPowerRise_IsNotMistakenForAnOutlier()
    {
        var history = new[]
        {
            Observation(0, 24, 82, 126),
            Observation(1, 35, 110, 130),
            Observation(2, 55, 160, 136),
            Observation(3, 75, 210, 142),
            Observation(4, 82, 230, 146),
        };

        var data = PerformanceTimelineProjection.Build(history, 4);
        var power = Assert.Single(data.Lanes, lane => lane.Metric == PerformanceTimelineMetric.PackagePower);

        Assert.All(power.Points, point => Assert.NotNull(point.Y));
        Assert.True(power.DomainMax >= 230);
    }

    [Fact]
    public void Build_IsolatedPerformanceSpike_DoesNotFlattenPerformanceLane()
    {
        var history = new[]
        {
            Observation(0, 24, 82, 126),
            Observation(1, 25, 84, 128),
            Observation(2, 26, 86, 240),
            Observation(3, 27, 83, 129),
            Observation(4, 28, 86, 131),
        };

        var data = PerformanceTimelineProjection.Build(history, 4);
        var performance = Assert.Single(data.Lanes, lane => lane.Metric == PerformanceTimelineMetric.EffectiveClock);

        Assert.True(performance.DomainMax <= 170, $"Unexpected outlier-expanded domain {performance.DomainMin:0.#}-{performance.DomainMax:0.#}%");
        Assert.Null(performance.Points[2].Y);
    }

    private static OperatingObservation Observation(int seconds, double pressure, double watts, double performancePercent) =>
        new(T0.AddSeconds(seconds), pressure, watts, null, 8, 16, EnvelopeZone.Efficient, null, EnvelopeDecisionKind.None, performancePercent);
}
