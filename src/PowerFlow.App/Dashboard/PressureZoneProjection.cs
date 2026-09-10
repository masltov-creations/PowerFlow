using PowerFlow.Core.Envelope;

namespace PowerFlow.App.Dashboard;

public sealed record PressureZoneBand(EnvelopeZone Zone, double MinimumPercent, double MaximumPercent);
public sealed record PressureZoneContext(double EcoThreshold, double EfficientThreshold, double ResponsiveThreshold, IReadOnlyList<PressureZoneBand> Bands);

public static class PressureZoneProjection
{
    public static PressureZoneContext Build(OperatingEnvelope learnedEnvelope, EnvelopeTuning candidateTuning)
    {
        ArgumentNullException.ThrowIfNull(learnedEnvelope);
        ArgumentNullException.ThrowIfNull(candidateTuning);
        var effective = candidateTuning.ApplyTo(learnedEnvelope);
        var eco = Math.Clamp(effective.EcoCeilingPressure, 0d, 100d);
        var efficient = Math.Clamp(effective.EfficientCeilingPressure, eco, 100d);
        var responsive = Math.Clamp(effective.ResponsiveCeilingPressure, efficient, 100d);
        return new PressureZoneContext(eco, efficient, responsive, new[]
        {
            new PressureZoneBand(EnvelopeZone.Eco, 0d, eco),
            new PressureZoneBand(EnvelopeZone.Efficient, eco, efficient),
            new PressureZoneBand(EnvelopeZone.Responsive, efficient, responsive),
            new PressureZoneBand(EnvelopeZone.Boost, responsive, 100d)
        });
    }
}