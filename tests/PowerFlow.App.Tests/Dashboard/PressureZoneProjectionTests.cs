using PowerFlow.App.Dashboard;
using PowerFlow.Core.Envelope;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class PressureZoneProjectionTests
{
    [Fact]
    public void Build_UsesEffectiveCandidateThresholdsOnExactZeroToHundredScale()
    {
        var learned = new OperatingEnvelope(20, 50, 80, 25, 45, 70);
        var tuning = new EnvelopeTuning(EnvelopeTuningLayer.Tuned, ecoCeilingPressure: 25, efficientCeilingPressure: 55, responsiveCeilingPressure: 85);
        var context = PressureZoneProjection.Build(learned, tuning);
        Assert.Equal(25, context.EcoThreshold);
        Assert.Equal(55, context.EfficientThreshold);
        Assert.Equal(85, context.ResponsiveThreshold);
        Assert.Collection(context.Bands,
            band => { Assert.Equal(EnvelopeZone.Eco, band.Zone); Assert.Equal(0, band.MinimumPercent); Assert.Equal(25, band.MaximumPercent); },
            band => { Assert.Equal(EnvelopeZone.Efficient, band.Zone); Assert.Equal(25, band.MinimumPercent); Assert.Equal(55, band.MaximumPercent); },
            band => { Assert.Equal(EnvelopeZone.Responsive, band.Zone); Assert.Equal(55, band.MinimumPercent); Assert.Equal(85, band.MaximumPercent); },
            band => { Assert.Equal(EnvelopeZone.Boost, band.Zone); Assert.Equal(85, band.MinimumPercent); Assert.Equal(100, band.MaximumPercent); });
    }
}