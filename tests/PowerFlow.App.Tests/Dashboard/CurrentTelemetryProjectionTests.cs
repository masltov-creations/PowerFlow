using PowerFlow.App.Dashboard;
using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class CurrentTelemetryProjectionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resolve_UsesLatestControllerStateButFreshestValidRichMetrics()
    {
        var observations = new[]
        {
            Observation(T0, 20, 52, 3450, 6, 16, EnvelopeZone.Efficient),
            Observation(T0.AddSeconds(2), 35, null, null, null, null, EnvelopeZone.Responsive)
        };

        var current = CurrentTelemetryProjection.Resolve(observations, TimeSpan.FromSeconds(10));

        Assert.NotNull(current);
        Assert.Equal(35, current!.Latest.CpuPressurePercent);
        Assert.Equal(EnvelopeZone.Responsive, current.Latest.Zone);
        Assert.Equal(52, current.PackageWatts);
        Assert.Equal(3450, current.EffectiveClockMhz);
        Assert.Equal(6, current.ActiveCores);
        Assert.Equal(16, current.TotalCores);
    }

    [Fact]
    public void Resolve_DoesNotPresentRichMetricsPastFreshnessBoundary()
    {
        var observations = new[]
        {
            Observation(T0, 20, 52, 3450, 6, 16, EnvelopeZone.Efficient),
            Observation(T0.AddSeconds(11), 35, null, null, null, null, EnvelopeZone.Responsive)
        };

        var current = CurrentTelemetryProjection.Resolve(observations, TimeSpan.FromSeconds(10));

        Assert.NotNull(current);
        Assert.Null(current!.PackageWatts);
        Assert.Null(current.EffectiveClockMhz);
        Assert.Null(current.ActiveCores);
        Assert.Null(current.TotalCores);
    }

    [Fact]
    public void Resolve_SelectsEachRichMetricIndependently()
    {
        var observations = new[]
        {
            Observation(T0, 20, 48, 3200, 4, 16, EnvelopeZone.Efficient),
            Observation(T0.AddSeconds(2), 25, null, 3550, null, null, EnvelopeZone.Efficient),
            Observation(T0.AddSeconds(4), 30, 61, null, 7, 16, EnvelopeZone.Efficient)
        };

        var current = CurrentTelemetryProjection.Resolve(observations, TimeSpan.FromSeconds(10));

        Assert.NotNull(current);
        Assert.Equal(61, current!.PackageWatts);
        Assert.Equal(3550, current.EffectiveClockMhz);
        Assert.Equal(7, current.ActiveCores);
        Assert.Equal(16, current.TotalCores);
    }

    private static OperatingObservation Observation(DateTimeOffset at, double cpu, double? watts, double? mhz, int? active, int? total, EnvelopeZone zone) =>
        new(at, cpu, watts, mhz, active, total, zone, null, EnvelopeDecisionKind.None);
}