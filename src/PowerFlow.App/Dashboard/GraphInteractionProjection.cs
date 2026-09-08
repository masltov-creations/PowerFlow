using PowerFlow.Core.Policy;

namespace PowerFlow.App.Dashboard;

public sealed record HoverTelemetry(DateTimeOffset At, double CpuPercent, double? PackageWatts, double? AverageMhz, PowerState State);

public static class TelemetryHoverProjection
{
    public static HoverTelemetry? Interpolate(IReadOnlyList<DashboardSample> samples, double normalizedX, DateTimeOffset latest, double windowSeconds)
    {
        if (samples.Count == 0 || windowSeconds <= 0) return null;
        var x = Math.Clamp(normalizedX, 0, 1);
        var target = latest.AddSeconds((x - 1) * windowSeconds);
        var ordered = samples.OrderBy(s => s.At).ToArray();
        if (target <= ordered[0].At) return FromSample(ordered[0]);
        if (target >= ordered[^1].At) return FromSample(ordered[^1]);

        for (var i = 1; i < ordered.Length; i++)
        {
            var right = ordered[i];
            if (target > right.At) continue;
            var left = ordered[i - 1];
            var span = (right.At - left.At).TotalMilliseconds;
            var t = span <= 0 ? 0 : Math.Clamp((target - left.At).TotalMilliseconds / span, 0, 1);
            return new HoverTelemetry(
                target,
                Lerp(left.CpuPercent, right.CpuPercent, t),
                LerpNullable(left.PackageWatts, right.PackageWatts, t),
                LerpNullable(left.AverageMhz, right.AverageMhz, t),
                t < 0.5 ? left.State : right.State);
        }
        return FromSample(ordered[^1]);
    }

    private static HoverTelemetry FromSample(DashboardSample s) => new(s.At, s.CpuPercent, s.PackageWatts, s.AverageMhz, s.State);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static double? LerpNullable(double? a, double? b, double t) => (a, b) switch
    {
        (double av, double bv) => Lerp(av, bv, t),
        (double av, null) => av,
        (null, double bv) => bv,
        _ => null
    };
}

public static class ThresholdDragProjection
{
    public const double MinimumGapPercent = 1;

    public static double PercentFromY(double y, double plotTop, double plotHeight)
    {
        if (plotHeight <= 0) return 0;
        return Math.Clamp((plotTop + plotHeight - y) / plotHeight * 100d, 0, 100);
    }

    public static double ClampQuiet(double requested, double promotePercent) =>
        Math.Clamp(requested, 0, Math.Max(0, promotePercent - MinimumGapPercent));

    public static double ClampPromotion(double requested, double quietPercent) =>
        Math.Clamp(requested, Math.Min(100, quietPercent + MinimumGapPercent), 100);
}

public sealed class ThresholdsChangedEventArgs(double quietPercent, double promotionPercent) : EventArgs
{
    public double QuietPercent { get; } = quietPercent;
    public double PromotionPercent { get; } = promotionPercent;
}