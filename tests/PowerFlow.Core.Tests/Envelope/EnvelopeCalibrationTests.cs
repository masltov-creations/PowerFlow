using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.Core.Tests.Envelope;

public sealed class EnvelopeCalibrationTests
{
    [Fact]
    public void Calibrate_WithInsufficientEvidence_ReturnsConservativeLowConfidenceDefaults()
    {
        var observations = Enumerable.Range(0, 8)
            .Select(i => Observation(i, 20 + i, 35 + i, 1800 + i * 20))
            .ToArray();

        var result = EnvelopeCalibration.Calibrate(observations);

        Assert.Equal(EnvelopeConfidence.Low, result.Confidence);
        Assert.Equal(8, result.SampleCount);
        Assert.Equal(25, result.Envelope.EcoCeilingPressure);
        Assert.Equal(55, result.Envelope.EfficientCeilingPressure);
        Assert.Equal(80, result.Envelope.ResponsiveCeilingPressure);
        Assert.Null(result.SustainedEfficiencyFrontierWatts);
    }

    [Fact]
    public void Calibrate_WithCoveredObservations_ProducesMonotonicLearnedBoundariesAndPowerFrontiers()
    {
        var observations = Enumerable.Range(0, 96)
            .Select(i => Observation(i, 8 + (i % 86), 28 + (i % 48), 1500 + (i % 32) * 80))
            .ToArray();

        var result = EnvelopeCalibration.Calibrate(observations);

        Assert.NotEqual(EnvelopeConfidence.Low, result.Confidence);
        Assert.True(result.Envelope.EcoCeilingPressure < result.Envelope.EfficientCeilingPressure);
        Assert.True(result.Envelope.EfficientCeilingPressure < result.Envelope.ResponsiveCeilingPressure);
        Assert.InRange(result.Envelope.ResponsiveCeilingPressure, 1, 100);
        Assert.NotNull(result.Envelope.EcoPowerFrontierWatts);
        Assert.NotNull(result.Envelope.EfficientPowerFrontierWatts);
        Assert.NotNull(result.Envelope.ResponsivePowerFrontierWatts);
        Assert.True(result.Envelope.EcoPowerFrontierWatts < result.Envelope.EfficientPowerFrontierWatts);
        Assert.True(result.Envelope.EfficientPowerFrontierWatts < result.Envelope.ResponsivePowerFrontierWatts);
    }

    [Fact]
    public void Calibrate_IsRobustToSingleExtremePowerOutlier()
    {
        var normal = Enumerable.Range(0, 80)
            .Select(i => Observation(i, 25 + (i % 60), 36 + (i % 30), 1900 + (i % 20) * 100))
            .ToList();
        var baseline = EnvelopeCalibration.Calibrate(normal);
        normal.Add(Observation(81, 90, 5000, 5000));

        var withOutlier = EnvelopeCalibration.Calibrate(normal);

        Assert.NotNull(baseline.Envelope.ResponsivePowerFrontierWatts);
        Assert.NotNull(withOutlier.Envelope.ResponsivePowerFrontierWatts);
        Assert.InRange(Math.Abs(withOutlier.Envelope.ResponsivePowerFrontierWatts!.Value - baseline.Envelope.ResponsivePowerFrontierWatts!.Value), 0, 8);
    }

    [Fact]
    public void Calibrate_ConfidenceRisesWithUsableCoverage()
    {
        var medium = Enumerable.Range(0, 36)
            .Select(i => Observation(i, 10 + i * 2, 30 + i, 1600 + i * 70))
            .ToArray();
        var high = Enumerable.Range(0, 180)
            .Select(i => Observation(i, 5 + (i % 92), 25 + (i % 75), 1400 + (i % 38) * 90))
            .ToArray();

        Assert.Equal(EnvelopeConfidence.Medium, EnvelopeCalibration.Calibrate(medium).Confidence);
        Assert.Equal(EnvelopeConfidence.High, EnvelopeCalibration.Calibrate(high).Confidence);
    }

    [Fact]
    public void Calibrate_RandomValidInputs_NeverProduceInvalidEnvelopeOrNonFiniteFrontiers()
    {
        var random = new Random(17);
        for (var run = 0; run < 40; run++)
        {
            var count = random.Next(20, 220);
            var observations = Enumerable.Range(0, count).Select(i =>
                Observation(i, random.NextDouble() * 100, 20 + random.NextDouble() * 160, 900 + random.NextDouble() * 4200)).ToArray();

            var result = EnvelopeCalibration.Calibrate(observations);

            Assert.True(result.Envelope.EcoCeilingPressure < result.Envelope.EfficientCeilingPressure);
            Assert.True(result.Envelope.EfficientCeilingPressure < result.Envelope.ResponsiveCeilingPressure);
            foreach (var value in new[] { result.Envelope.EcoPowerFrontierWatts, result.Envelope.EfficientPowerFrontierWatts, result.Envelope.ResponsivePowerFrontierWatts })
                Assert.True(value is null || double.IsFinite(value.Value));
        }
    }

    private static OperatingObservation Observation(int second, double pressure, double watts, double mhz) => new(
        new DateTimeOffset(2026, 9, 9, 18, 0, 0, TimeSpan.Zero).AddSeconds(second),
        pressure,
        watts,
        mhz,
        null,
        16,
        EnvelopeZone.Efficient,
        null,
        EnvelopeDecisionKind.None);
}
