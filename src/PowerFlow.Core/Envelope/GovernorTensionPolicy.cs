namespace PowerFlow.Core.Envelope;

public enum GovernorTensionReference
{
    SaverBias,
    BalancedEfficient,
    BalancedPerformance,
    PerformanceBias
}

public sealed record GovernorTensionModel(
    double TensionPercent,
    GovernorTensionReference Reference,
    OperatingEnvelope EffectiveEnvelope,
    PerformanceEntitlement EffectiveEntitlement);

public static class GovernorTensionPolicy
{
    private const double MinimumBoundaryGap = 5d;

    public static GovernorTensionModel Resolve(
        double tensionPercent,
        OperatingEnvelope learnedEnvelope,
        PerformanceEntitlement baseEntitlement)
    {
        ArgumentNullException.ThrowIfNull(learnedEnvelope);
        ArgumentNullException.ThrowIfNull(baseEntitlement);

        var tension = double.IsFinite(tensionPercent) ? Math.Clamp(tensionPercent, 0d, 100d) : 50d;
        var p = tension / 100d;
        var shift = 10d - 20d * p;

        var eco = Math.Clamp(learnedEnvelope.EcoCeilingPressure + shift, 0d, 85d);
        var efficient = Math.Clamp(learnedEnvelope.EfficientCeilingPressure + shift, eco + MinimumBoundaryGap, 90d);
        var responsive = Math.Clamp(learnedEnvelope.ResponsiveCeilingPressure + shift, efficient + MinimumBoundaryGap, 95d);

        var envelope = new OperatingEnvelope(
            eco,
            efficient,
            responsive,
            learnedEnvelope.EcoPowerFrontierWatts,
            learnedEnvelope.EfficientPowerFrontierWatts,
            learnedEnvelope.ResponsivePowerFrontierWatts);

        var qualificationFactor = 1.75d - 1.5d * p;
        var leaseFactor = 0.6d + 0.8d * p;
        var releaseFactor = 0.5d + 1.0d * p;
        var entitlement = new PerformanceEntitlement(
            baseEntitlement.MaximumZone,
            Scale(baseEntitlement.QualificationDuration, qualificationFactor),
            Scale(baseEntitlement.LeaseDuration, leaseFactor),
            Scale(baseEntitlement.ReleaseHysteresis, releaseFactor),
            baseEntitlement.FollowChildren);

        return new GovernorTensionModel(tension, NearestReference(tension), envelope, entitlement);
    }

    private static TimeSpan Scale(TimeSpan duration, double factor) =>
        TimeSpan.FromTicks((long)Math.Round(duration.Ticks * factor, MidpointRounding.AwayFromZero));

    private static GovernorTensionReference NearestReference(double tension)
    {
        var anchors = new (double Position, GovernorTensionReference Reference)[]
        {
            (0d, GovernorTensionReference.SaverBias),
            (33d, GovernorTensionReference.BalancedEfficient),
            (66d, GovernorTensionReference.BalancedPerformance),
            (100d, GovernorTensionReference.PerformanceBias)
        };
        return anchors.MinBy(anchor => Math.Abs(anchor.Position - tension)).Reference;
    }
}
