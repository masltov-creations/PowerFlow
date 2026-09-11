using PowerFlow.App.Dashboard;
using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PerformanceTimelineProjectionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 9, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_UsesOneSharedTimeAxisAcrossAllKpiLanes()
    {
        var history = new[]
        {
            Observation(0, 20, 30, 1800, 2, 16),
            Observation(10, 40, 50, 2600, 5, 16),
            Observation(20, 80, 90, 4200, 11, 16)
        };

        var data = PerformanceTimelineProjection.Build(history, 20);

        Assert.Equal(4, data.Lanes.Count);
        var expectedX = data.Lanes[0].Points.Select(x => x.X).ToArray();
        foreach (var lane in data.Lanes)
            Assert.Equal(expectedX, lane.Points.Select(x => x.X).ToArray());
        Assert.Equal(new[] { 0d, .5d, 1d }, expectedX, new DoubleComparer(.0001));
    }

    [Fact]
    public void Build_UsesTruthfulLaneSpecificDomainsRatherThanOneMisleadingYAxis()
    {
        var history = new[]
        {
            Observation(0, 50, 50, 2500, 8, 16),
            Observation(10, 100, 100, 5000, 16, 16)
        };

        var data = PerformanceTimelineProjection.Build(history, 10);

        Assert.Equal(100, Lane(data, PerformanceTimelineMetric.CpuPressure).DomainMax);
        Assert.Equal(100, Lane(data, PerformanceTimelineMetric.PackagePower).DomainMax);
        Assert.Equal(125, Lane(data, PerformanceTimelineMetric.EffectiveClock).DomainMax);
        Assert.Equal(16, Lane(data, PerformanceTimelineMetric.ActiveCores).DomainMax);
        Assert.Equal(.5, Lane(data, PerformanceTimelineMetric.CpuPressure).Points[0].Y!.Value, 3);
        Assert.Equal(.5, Lane(data, PerformanceTimelineMetric.PackagePower).Points[0].Y!.Value, 3);
        Assert.Equal(.5, Lane(data, PerformanceTimelineMetric.EffectiveClock).Points[0].Y!.Value, 3);
        Assert.Equal(.5, Lane(data, PerformanceTimelineMetric.ActiveCores).Points[0].Y!.Value, 3);
    }

    [Fact]
    public void Build_PreservesUnavailableTelemetryAsGaps()
    {
        var history = new[]
        {
            Observation(0, 30, null, 1800, null, 16),
            Observation(10, 40, 45, null, 4, 16)
        };

        var data = PerformanceTimelineProjection.Build(history, 10);

        Assert.Null(Lane(data, PerformanceTimelineMetric.PackagePower).Points[0].Y);
        Assert.Null(Lane(data, PerformanceTimelineMetric.EffectiveClock).Points[1].Y);
        Assert.Null(Lane(data, PerformanceTimelineMetric.ActiveCores).Points[0].Y);
        Assert.NotNull(Lane(data, PerformanceTimelineMetric.CpuPressure).Points[0].Y);
    }

    [Fact]
    public void Build_ClipsToRequestedWindowAndKeepsOriginalObservationIndices()
    {
        var history = new[]
        {
            Observation(0, 10, 30, 1700, 2, 16),
            Observation(30, 20, 35, 1800, 3, 16),
            Observation(60, 30, 40, 1900, 4, 16),
            Observation(90, 40, 45, 2000, 5, 16)
        };

        var data = PerformanceTimelineProjection.Build(history, 45);

        Assert.Equal(2, data.Lanes[0].Points.Count);
        Assert.Equal(new[] { 2, 3 }, data.Lanes[0].Points.Select(x => x.ObservationIndex));
        Assert.Equal(T0.AddSeconds(45), data.WindowStart);
        Assert.Equal(T0.AddSeconds(90), data.Latest);
    }

    [Fact]
    public void FindNearestObservationIndex_UsesSharedCursorPosition()
    {
        var history = new[]
        {
            Observation(0, 10, 30, 1700, 2, 16),
            Observation(10, 20, 35, 1800, 3, 16),
            Observation(20, 30, 40, 1900, 4, 16)
        };
        var data = PerformanceTimelineProjection.Build(history, 20);

        Assert.Equal(1, PerformanceTimelineProjection.FindNearestObservationIndex(data, .48));
        Assert.Equal(2, PerformanceTimelineProjection.FindNearestObservationIndex(data, .92));
    }

    [Fact]
    public void Build_ProjectsActorAndDecisionEvidenceOnSameTimeAxis()
    {
        var history = new[]
        {
            Observation(0, 20, 30, 1800, 2, 16),
            new OperatingObservation(T0.AddSeconds(10), 90, 75, 3900, 8, 16, EnvelopeZone.Responsive, "chrome.exe", EnvelopeDecisionKind.Brake)
        };

        var data = PerformanceTimelineProjection.Build(history, 10);

        var marker = Assert.Single(data.Events);
        Assert.Equal(1, marker.ObservationIndex);
        Assert.Equal(1, marker.X, 3);
        Assert.Equal("chrome.exe", marker.Actor);
        Assert.Equal(EnvelopeDecisionKind.Brake, marker.Decision);
    }

    private static TimelineLaneProjection Lane(PerformanceTimelineData data, PerformanceTimelineMetric metric) => Assert.Single(data.Lanes, x => x.Metric == metric);

    private static OperatingObservation Observation(int seconds, double pressure, double? watts, double? mhz, int? active, int? total) =>
        new(T0.AddSeconds(seconds), pressure, watts, mhz, active, total, EnvelopeZone.Efficient, null, EnvelopeDecisionKind.None, mhz is double value ? value / 40d : null);

    private sealed class DoubleComparer(double tolerance) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) <= tolerance;
        public int GetHashCode(double obj) => 0;
    }
}
