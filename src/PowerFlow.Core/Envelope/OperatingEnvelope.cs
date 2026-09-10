namespace PowerFlow.Core.Envelope;

public sealed record OperatingEnvelope
{
    public OperatingEnvelope(
        double ecoCeilingPressure,
        double efficientCeilingPressure,
        double responsiveCeilingPressure,
        double? ecoPowerFrontierWatts,
        double? efficientPowerFrontierWatts,
        double? responsivePowerFrontierWatts)
    {
        if (!double.IsFinite(ecoCeilingPressure) || ecoCeilingPressure < 0 || ecoCeilingPressure >= efficientCeilingPressure)
            throw new ArgumentOutOfRangeException(nameof(ecoCeilingPressure));
        if (!double.IsFinite(efficientCeilingPressure) || efficientCeilingPressure <= ecoCeilingPressure || efficientCeilingPressure >= responsiveCeilingPressure)
            throw new ArgumentOutOfRangeException(nameof(efficientCeilingPressure));
        if (!double.IsFinite(responsiveCeilingPressure) || responsiveCeilingPressure <= efficientCeilingPressure || responsiveCeilingPressure > 100)
            throw new ArgumentOutOfRangeException(nameof(responsiveCeilingPressure));

        EcoCeilingPressure = ecoCeilingPressure;
        EfficientCeilingPressure = efficientCeilingPressure;
        ResponsiveCeilingPressure = responsiveCeilingPressure;
        EcoPowerFrontierWatts = ValidPower(ecoPowerFrontierWatts);
        EfficientPowerFrontierWatts = ValidPower(efficientPowerFrontierWatts);
        ResponsivePowerFrontierWatts = ValidPower(responsivePowerFrontierWatts);
    }

    public double EcoCeilingPressure { get; }
    public double EfficientCeilingPressure { get; }
    public double ResponsiveCeilingPressure { get; }
    public double? EcoPowerFrontierWatts { get; }
    public double? EfficientPowerFrontierWatts { get; }
    public double? ResponsivePowerFrontierWatts { get; }

    public EnvelopeZone ZoneForPressure(double pressure)
    {
        var value = double.IsFinite(pressure) ? Math.Clamp(pressure, 0, 100) : 0;
        if (value <= EcoCeilingPressure) return EnvelopeZone.Eco;
        if (value <= EfficientCeilingPressure) return EnvelopeZone.Efficient;
        if (value <= ResponsiveCeilingPressure) return EnvelopeZone.Responsive;
        return EnvelopeZone.Boost;
    }

    private static double? ValidPower(double? value) => value is > 0 and double watts && double.IsFinite(watts) ? watts : null;
}
