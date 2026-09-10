using PowerFlow.Core.Envelope;

namespace PowerFlow.App.Dashboard;

public static class TimelinePolicyInteraction
{
    public static EnvelopeTuning ApplyDrag(
        EnvelopeTuning current,
        TimelinePolicyHandleKind kind,
        double normalizedX,
        double normalizedCanvasY,
        OperatingEnvelope learnedEnvelope,
        PerformanceEntitlement learnedEntitlement,
        double windowSeconds)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(learnedEnvelope);
        ArgumentNullException.ThrowIfNull(learnedEntitlement);
        var layer = current.ManualOverrideZone is null ? EnvelopeTuningLayer.Tuned : EnvelopeTuningLayer.Override;

        if (kind is TimelinePolicyHandleKind.EcoPressure or TimelinePolicyHandleKind.EfficientPressure or TimelinePolicyHandleKind.ResponsivePressure)
        {
            var pressure = Math.Clamp((1d - Math.Clamp(normalizedCanvasY / .25d, 0d, 1d)) * 100d, 0d, 100d);
            var effective = current.ApplyTo(learnedEnvelope);
            return kind switch
            {
                TimelinePolicyHandleKind.EcoPressure => Copy(current, layer, eco: Math.Clamp(pressure, 0d, effective.EfficientCeilingPressure - 1d)),
                TimelinePolicyHandleKind.EfficientPressure => Copy(current, layer, efficient: Math.Clamp(pressure, effective.EcoCeilingPressure + 1d, effective.ResponsiveCeilingPressure - 1d)),
                _ => Copy(current, layer, responsive: Math.Clamp(pressure, effective.EfficientCeilingPressure + 1d, 100d))
            };
        }

        var seconds = (1d - Math.Clamp(normalizedX, 0d, 1d)) * Math.Max(1d, windowSeconds);
        return kind switch
        {
            TimelinePolicyHandleKind.QualificationDuration => Copy(current, layer, qualification: TimeSpan.FromSeconds(Math.Clamp(seconds, 0d, 60d))),
            TimelinePolicyHandleKind.LeaseDuration => Copy(current, layer, lease: TimeSpan.FromSeconds(Math.Clamp(seconds, 1d, 60d))),
            TimelinePolicyHandleKind.ReleaseHysteresis => Copy(current, layer, release: TimeSpan.FromSeconds(Math.Clamp(seconds, 0d, 60d))),
            _ => current
        };
    }

    private static EnvelopeTuning Copy(
        EnvelopeTuning source,
        EnvelopeTuningLayer layer,
        double? eco = null,
        double? efficient = null,
        double? responsive = null,
        TimeSpan? qualification = null,
        TimeSpan? lease = null,
        TimeSpan? release = null)
    {
        return new EnvelopeTuning(
            layer,
            eco ?? source.EcoCeilingPressure,
            efficient ?? source.EfficientCeilingPressure,
            responsive ?? source.ResponsiveCeilingPressure,
            source.MaximumZone,
            qualification ?? source.QualificationDuration,
            lease ?? source.LeaseDuration,
            release ?? source.ReleaseHysteresis,
            source.ManualOverrideZone);
    }
}