namespace PowerFlow.Core.Envelope;

public enum EnvelopeTuningLayer
{
    Learned,
    Tuned,
    Override
}

public sealed record EnvelopeTuning
{
    public EnvelopeTuning(
        EnvelopeTuningLayer layer,
        double? ecoCeilingPressure = null,
        double? efficientCeilingPressure = null,
        double? responsiveCeilingPressure = null,
        EnvelopeZone? maximumZone = null,
        TimeSpan? qualificationDuration = null,
        TimeSpan? leaseDuration = null,
        TimeSpan? releaseHysteresis = null,
        EnvelopeZone? manualOverrideZone = null)
    {
        Layer = layer;
        EcoCeilingPressure = ValidBoundary(ecoCeilingPressure, nameof(ecoCeilingPressure));
        EfficientCeilingPressure = ValidBoundary(efficientCeilingPressure, nameof(efficientCeilingPressure));
        ResponsiveCeilingPressure = ValidBoundary(responsiveCeilingPressure, nameof(responsiveCeilingPressure));
        MaximumZone = maximumZone;
        QualificationDuration = ValidNonNegative(qualificationDuration, nameof(qualificationDuration));
        LeaseDuration = ValidPositive(leaseDuration, nameof(leaseDuration));
        ReleaseHysteresis = ValidNonNegative(releaseHysteresis, nameof(releaseHysteresis));
        ManualOverrideZone = manualOverrideZone;
    }

    public EnvelopeTuningLayer Layer { get; }
    public double? EcoCeilingPressure { get; }
    public double? EfficientCeilingPressure { get; }
    public double? ResponsiveCeilingPressure { get; }
    public EnvelopeZone? MaximumZone { get; }
    public TimeSpan? QualificationDuration { get; }
    public TimeSpan? LeaseDuration { get; }
    public TimeSpan? ReleaseHysteresis { get; }
    public EnvelopeZone? ManualOverrideZone { get; }

    public static EnvelopeTuning Learned { get; } = new(EnvelopeTuningLayer.Learned);

    public OperatingEnvelope ApplyTo(OperatingEnvelope learned)
    {
        ArgumentNullException.ThrowIfNull(learned);
        return new OperatingEnvelope(
            EcoCeilingPressure ?? learned.EcoCeilingPressure,
            EfficientCeilingPressure ?? learned.EfficientCeilingPressure,
            ResponsiveCeilingPressure ?? learned.ResponsiveCeilingPressure,
            learned.EcoPowerFrontierWatts,
            learned.EfficientPowerFrontierWatts,
            learned.ResponsivePowerFrontierWatts);
    }

    public PerformanceEntitlement ApplyTo(PerformanceEntitlement learned)
    {
        ArgumentNullException.ThrowIfNull(learned);
        return new PerformanceEntitlement(
            MaximumZone ?? learned.MaximumZone,
            QualificationDuration ?? learned.QualificationDuration,
            LeaseDuration ?? learned.LeaseDuration,
            ReleaseHysteresis ?? learned.ReleaseHysteresis,
            learned.FollowChildren);
    }

    private static double? ValidBoundary(double? value, string name)
    {
        if (value is null) return null;
        if (!double.IsFinite(value.Value) || value.Value is < 0 or > 100)
            throw new ArgumentOutOfRangeException(name);
        return value;
    }

    private static TimeSpan? ValidNonNegative(TimeSpan? value, string name)
    {
        if (value is TimeSpan t && t < TimeSpan.Zero) throw new ArgumentOutOfRangeException(name);
        return value;
    }

    private static TimeSpan? ValidPositive(TimeSpan? value, string name)
    {
        if (value is TimeSpan t && t <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(name);
        return value;
    }
}