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

    private static OperatingObservation At(DateTimeOffset at, double pressure)
        => new(at, pressure, null, null, null, null,
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
    public void Build_KeepsModelZoneProfileAndAuthorityIndependent()
    {
        var state = HarmonicRingProjection.Build(
            new[] { O(0, 55) },
            Now,
            confirmedAppliedProfile: "PERF",
            confirmedAppliedAuthority: "Manual selection");

        Assert.Equal(EnvelopeZone.Efficient, state.ModelZone);
        Assert.Equal("PERF", state.ConfirmedAppliedProfile);
        Assert.Equal("Manual selection", state.ConfirmedAppliedAuthority);
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
    public void Build_TreatsFreshnessBoundaryAsCurrent()
    {
        var state = HarmonicRingProjection.Build(new[] { O(8, 10, 42), O(0, 70) }, Now);

        Assert.Equal(RingReadingQuality.Current, state.PackageWatts.Quality);
        Assert.Equal(8, state.PackageWatts.Age!.Value.TotalSeconds);
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
    public void Build_TrendDoesNotBridgeLongObservationGaps()
    {
        var observations = new[] { O(9, 10), O(1, 30), O(0, 80) };

        Assert.Equal(RingPressureTrend.Unknown, HarmonicRingProjection.Build(observations, Now).PressureTrend);
    }

    [Fact]
    public void Build_UsesIndependentMetricFreshnessUnitsAndAge()
    {
        var observations = new[] { O(12, 40, 33, 4000, 4, 8), O(2, 52) };
        var result = HarmonicRingProjection.Build(observations, Now);

        Assert.Equal("pressure %", result.Pressure.Unit);
        Assert.Equal(2, result.Pressure.Age!.Value.TotalSeconds);
        Assert.True(result.Pressure.IsCurrent);
        Assert.Equal("CPU busy %", result.CpuUsage.Unit);
        Assert.False(result.CpuUsage.IsAvailable);
        Assert.Equal("W", result.PackageWatts.Unit);
        Assert.Equal(12, result.PackageWatts.Age!.Value.TotalSeconds);
        Assert.True(result.PackageWatts.IsAvailable);
        Assert.Equal(RingReadingQuality.Stale, result.PackageWatts.Quality);
        Assert.Equal("MHz", result.EffectiveClockMhz.Unit);
        Assert.Equal("cores", result.AwakeCores.Unit);
        Assert.Equal("cores", result.TotalCores.Unit);
        Assert.Equal(Now.UtcDateTime.Ticks, result.Sequence);
    }

    [Fact]
    public void Build_KeepsMissingAndZeroMetricsDistinct()
    {
        var result = HarmonicRingProjection.Build(new[]
        {
            new OperatingObservation(Now, 0, double.NaN, -1, 0, 0,
                EnvelopeZone.Efficient, null, EnvelopeDecisionKind.None)
        }, Now);

        Assert.Equal(0, result.Pressure.Value);
        Assert.True(result.Pressure.IsCurrent);
        Assert.False(result.PackageWatts.IsAvailable);
        Assert.Null(result.PackageWatts.Value);
        Assert.False(result.EffectiveClockMhz.IsAvailable);
        Assert.Null(result.AwakeCores.Value);
        Assert.Null(result.TotalCores.Value);
    }

    [Fact]
    public void Build_DeduplicatesEqualTimestampsDeterministically()
    {
        var duplicateAt = Now.AddSeconds(-1);
        var samples = new[] { At(duplicateAt, 20), At(duplicateAt, 80), O(0, 90) };

        var forward = HarmonicRingProjection.Build(samples, Now);
        var reversed = HarmonicRingProjection.Build(samples.Reverse().ToArray(), Now);

        Assert.Equal(forward.PressureHistory, reversed.PressureHistory);
        Assert.Equal(forward.Sequence, reversed.Sequence);
    }

    [Fact]
    public void Build_CanonicalizesEqualInstantsWithDifferentOffsets()
    {
        var utc = Now.AddSeconds(-1);
        var localOffset = utc.ToOffset(TimeSpan.FromHours(-7));
        var samples = new[] { At(utc, 50), At(localOffset, 50), O(0, 90) };

        var forward = HarmonicRingProjection.Build(samples, Now);
        var reversed = HarmonicRingProjection.Build(samples.Reverse().ToArray(), Now);

        Assert.Equal(forward.PressureHistory[0].At.Offset, reversed.PressureHistory[0].At.Offset);
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
    public void Build_BoundsDenseBurstsAndIsIndependentOfInputOrder()
    {
        var start = Now.AddSeconds(-60);
        var burst = Enumerable.Range(0, 1200)
            .Select(i => At(start.AddMilliseconds(i * 50), (i * 37) % 101))
            .ToArray();

        var forward = HarmonicRingProjection.Build(burst, Now, maximumPoints: 24);
        var reverse = HarmonicRingProjection.Build(burst.Reverse().ToArray(), Now, maximumPoints: 24);

        Assert.InRange(forward.PressureHistory.Count, 1, 24);
        Assert.Equal(start, forward.PressureHistory[0].At);
        Assert.Equal(start.AddMilliseconds(59_950), forward.PressureHistory[^1].At);
        Assert.Equal(forward.PressureHistory, reverse.PressureHistory);
    }

    [Fact]
    public void Build_DoesNotBridgeObservationGaps()
    {
        var result = HarmonicRingProjection.Build(new[] { O(20, 15), O(19, 20), O(1, 60), O(0, 65) }, Now);
        Assert.Equal(2, result.PressureHistory.Count(p => p.StartsSegment));
    }

    [Fact]
    public void Build_GapAtThresholdStaysContinuous()
    {
        var result = HarmonicRingProjection.Build(new[] { O(5, 15), O(0, 20) }, Now);

        Assert.Single(result.PressureHistory, point => point.StartsSegment);
    }

    [Fact]
    public void Build_ReturnsAnImmutablePressureHistory()
    {
        var result = HarmonicRingProjection.Build(new[] { O(2, 20), O(0, 40) }, Now);
        var list = Assert.IsAssignableFrom<IList<RingWavePoint>>(result.PressureHistory);

        Assert.Throws<NotSupportedException>(() => list[0] = list[1]);
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
