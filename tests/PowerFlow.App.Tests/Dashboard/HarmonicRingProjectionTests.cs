using PowerFlow.App.Dashboard.HarmonicRing;
using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class HarmonicRingProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static OperatingObservation O(int secondsAgo, double pressure, double? watts = null,
        double? mhz = null, int? active = null, int? total = null)
        => new(Now.AddSeconds(-secondsAgo), pressure, watts, mhz, active, total,
            EnvelopeZone.Efficient, null, EnvelopeDecisionKind.None);

    [Fact]
    public void Build_KeepsUnavailableUsageSeparateFromPressure()
    {
        var state = HarmonicRingProjection.Build(new[] { O(0, 78, 44, 4100, 9, 16) }, Now);
        Assert.Equal(78d, state.Pressure.Value);
        Assert.True(state.Pressure.IsCurrent);
        Assert.Null(state.CpuUsage.Value);
        Assert.Equal(RingReadingQuality.Missing, state.CpuUsage.Quality);
        Assert.Equal(44d, state.PackageWatts.Value);
        Assert.Equal(4100d, state.EffectiveClockMhz.Value);
        Assert.Equal(9d, state.AwakeCores.Value);
        Assert.Equal(16d, state.TotalCores.Value);
    }

    [Fact]
    public void Build_StaleWattsAreNotPretendedCurrent()
    {
        var state = HarmonicRingProjection.Build(new[] { O(15, 10, 42), O(0, 70) }, Now);
        Assert.Equal(RingReadingQuality.Stale, state.PackageWatts.Quality);
        Assert.Equal(42d, state.PackageWatts.Value);
        Assert.Equal(RingReadingQuality.Current, state.Pressure.Quality);
    }

    [Fact]
    public void Build_TrendCapturesBuildingEbbingAndDeadband()
    {
        var rising = new[] { O(9, 20), O(6, 22), O(3, 57), O(0, 62) };
        var falling = new[] { O(9, 81), O(6, 78), O(3, 28), O(0, 25) };
        var steady = new[] { O(9, 40), O(6, 42), O(3, 41), O(0, 40) };
        Assert.Equal(RingPressureTrend.Building, HarmonicRingProjection.Build(rising, Now).PressureTrend);
        Assert.Equal(RingPressureTrend.Ebbing, HarmonicRingProjection.Build(falling, Now).PressureTrend);
        Assert.Equal(RingPressureTrend.Steady, HarmonicRingProjection.Build(steady, Now).PressureTrend);
        Assert.Equal(RingPressureTrend.Unknown, HarmonicRingProjection.Build(new[] { O(0, 90) }, Now).PressureTrend);
    }

    [Fact]
    public void Build_BoundsHistoryAndPreservesEndpoints()
    {
        var observations = Enumerable.Range(0, 1200)
            .Select(i => O(i % 60, (i * 7) % 101)).ToArray();
        var result = HarmonicRingProjection.Build(observations, Now, maximumPoints: 24);
        Assert.InRange(result.PressureHistory.Count, 1, 24);
        Assert.Equal(Now.AddSeconds(-59), result.PressureHistory[0].At);
        Assert.Equal(Now, result.PressureHistory[^1].At);
    }

    [Fact]
    public void Build_DoesNotBridgeObservationGaps()
    {
        var result = HarmonicRingProjection.Build(new[] { O(20, 15), O(19, 20), O(1, 60), O(0, 65) }, Now);
        Assert.Equal(2, result.PressureHistory.Count(p => p.StartsSegment));
    }

    [Fact]
    public void Geometry_StaysWithinCircleAndBreaksOnGaps()
    {
        var snapshot = HarmonicRingProjection.Build(
            new[] { O(20, 10), O(19, 40), O(1, 90), O(0, 100) }, Now);
        var figures = HarmonicRingGeometry.BuildPressureFigures(snapshot.PressureHistory, 280);
        Assert.Equal(2, figures.Count);
        Assert.All(figures.SelectMany(f => f), p =>
        {
            Assert.InRange(p.X, 0, 280);
            Assert.InRange(p.Y, 0, 280);
        });
        Assert.Equal(16, HarmonicRingGeometry.CoreTickCenters(16, 280).Count);
    }
}
