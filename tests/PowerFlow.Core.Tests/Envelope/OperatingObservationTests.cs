using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.Core.Tests.Envelope;

public sealed class OperatingObservationTests
{
    [Fact]
    public void Observation_ClampsPressureAndPreservesUnavailableTelemetry()
    {
        var at = new DateTimeOffset(2026, 9, 9, 18, 30, 0, TimeSpan.Zero);
        var observation = new OperatingObservation(
            at,
            140,
            null,
            null,
            null,
            16,
            EnvelopeZone.Efficient,
            null,
            EnvelopeDecisionKind.None);

        Assert.Equal(at, observation.At);
        Assert.Equal(100, observation.CpuPressurePercent);
        Assert.Null(observation.PackageWatts);
        Assert.Null(observation.EffectiveClockMhz);
        Assert.Null(observation.ActiveCores);
        Assert.Equal(16, observation.TotalCores);
        Assert.Null(observation.Actor);
    }

    [Fact]
    public void Observation_RejectsNonsensicalTelemetryInsteadOfInventingValues()
    {
        var observation = new OperatingObservation(
            DateTimeOffset.UtcNow,
            -4,
            -20,
            -300,
            24,
            16,
            EnvelopeZone.Eco,
            "chrome.exe",
            EnvelopeDecisionKind.Brake);

        Assert.Equal(0, observation.CpuPressurePercent);
        Assert.Null(observation.PackageWatts);
        Assert.Null(observation.EffectiveClockMhz);
        Assert.Equal(16, observation.ActiveCores);
        Assert.Equal("chrome.exe", observation.Actor);
        Assert.Equal(EnvelopeDecisionKind.Brake, observation.Decision);
    }

    [Fact]
    public void Envelope_RequiresStrictlyOrderedSemanticBoundaries()
    {
        var envelope = new OperatingEnvelope(25, 55, 80, 30, 52, 84);

        Assert.Equal(EnvelopeZone.Eco, envelope.ZoneForPressure(12));
        Assert.Equal(EnvelopeZone.Efficient, envelope.ZoneForPressure(40));
        Assert.Equal(EnvelopeZone.Responsive, envelope.ZoneForPressure(70));
        Assert.Equal(EnvelopeZone.Boost, envelope.ZoneForPressure(92));

        Assert.Throws<ArgumentOutOfRangeException>(() => new OperatingEnvelope(60, 55, 80, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OperatingEnvelope(25, 90, 80, null, null, null));
    }
}
