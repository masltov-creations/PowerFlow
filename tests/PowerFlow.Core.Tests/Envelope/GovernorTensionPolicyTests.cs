using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.Core.Tests.Envelope;

public sealed class GovernorTensionPolicyTests
{
    private static readonly OperatingEnvelope Learned = new(20, 50, 80, 35, 65, 95);
    private static readonly PerformanceEntitlement BaseEntitlement = new(
        EnvelopeZone.Boost,
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(12),
        TimeSpan.FromSeconds(10),
        true);

    [Fact]
    public void Resolve_Tension50IsNeutral()
    {
        var model = GovernorTensionPolicy.Resolve(50, Learned, BaseEntitlement);

        Assert.Equal(50, model.TensionPercent);
        Assert.Equal(Learned, model.EffectiveEnvelope);
        Assert.Equal(BaseEntitlement, model.EffectiveEntitlement);
    }

    [Fact]
    public void Resolve_LowTensionPromotesLaterAndSettlesSooner()
    {
        var model = GovernorTensionPolicy.Resolve(0, Learned, BaseEntitlement);

        Assert.Equal(30, model.EffectiveEnvelope.EcoCeilingPressure);
        Assert.Equal(60, model.EffectiveEnvelope.EfficientCeilingPressure);
        Assert.Equal(90, model.EffectiveEnvelope.ResponsiveCeilingPressure);
        Assert.Equal(TimeSpan.FromSeconds(7), model.EffectiveEntitlement.QualificationDuration);
        Assert.Equal(TimeSpan.FromSeconds(7.2), model.EffectiveEntitlement.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), model.EffectiveEntitlement.ReleaseHysteresis);
    }

    [Fact]
    public void Resolve_HighTensionPromotesEarlierAndHoldsLonger()
    {
        var model = GovernorTensionPolicy.Resolve(100, Learned, BaseEntitlement);

        Assert.Equal(10, model.EffectiveEnvelope.EcoCeilingPressure);
        Assert.Equal(40, model.EffectiveEnvelope.EfficientCeilingPressure);
        Assert.Equal(70, model.EffectiveEnvelope.ResponsiveCeilingPressure);
        Assert.Equal(TimeSpan.FromSeconds(1), model.EffectiveEntitlement.QualificationDuration);
        Assert.Equal(TimeSpan.FromSeconds(16.8), model.EffectiveEntitlement.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(15), model.EffectiveEntitlement.ReleaseHysteresis);
    }

    [Fact]
    public void Resolve_PreservesAppMaximumZoneAndPowerEvidence()
    {
        var entitlement = BaseEntitlement with { MaximumZone = EnvelopeZone.Efficient };
        var model = GovernorTensionPolicy.Resolve(100, Learned, entitlement);

        Assert.Equal(EnvelopeZone.Efficient, model.EffectiveEntitlement.MaximumZone);
        Assert.Equal(Learned.EcoPowerFrontierWatts, model.EffectiveEnvelope.EcoPowerFrontierWatts);
        Assert.Equal(Learned.EfficientPowerFrontierWatts, model.EffectiveEnvelope.EfficientPowerFrontierWatts);
        Assert.Equal(Learned.ResponsivePowerFrontierWatts, model.EffectiveEnvelope.ResponsivePowerFrontierWatts);
    }

    [Fact]
    public void Resolve_ClampsTensionAndKeepsAtLeastFivePressurePointsBetweenBoundaries()
    {
        var tight = new OperatingEnvelope(2, 7, 12, null, null, null);
        var high = GovernorTensionPolicy.Resolve(250, tight, BaseEntitlement);
        var low = GovernorTensionPolicy.Resolve(-50, new OperatingEnvelope(85, 90, 95, null, null, null), BaseEntitlement);

        Assert.Equal(100, high.TensionPercent);
        Assert.Equal(0, low.TensionPercent);
        AssertOrdered(high.EffectiveEnvelope);
        AssertOrdered(low.EffectiveEnvelope);
    }

    [Theory]
    [InlineData(0, GovernorTensionReference.SaverBias)]
    [InlineData(30, GovernorTensionReference.BalancedEfficient)]
    [InlineData(60, GovernorTensionReference.BalancedPerformance)]
    [InlineData(100, GovernorTensionReference.PerformanceBias)]
    public void Resolve_ReportsNearestReferenceLandmark(double tension, GovernorTensionReference expected)
    {
        Assert.Equal(expected, GovernorTensionPolicy.Resolve(tension, Learned, BaseEntitlement).Reference);
    }

    private static void AssertOrdered(OperatingEnvelope envelope)
    {
        Assert.InRange(envelope.EcoCeilingPressure, 0, 85);
        Assert.True(envelope.EfficientCeilingPressure - envelope.EcoCeilingPressure >= 5);
        Assert.True(envelope.ResponsiveCeilingPressure - envelope.EfficientCeilingPressure >= 5);
        Assert.True(100 - envelope.ResponsiveCeilingPressure >= 5);
    }
}
