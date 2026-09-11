namespace PowerFlow.App.Telemetry;

public enum TelemetryCadenceMode
{
    Off,
    HiddenAuto,
    Visible
}

public static class TelemetryCadencePolicy
{
    public static readonly TimeSpan DefaultVisibleInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DefaultHiddenInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MinimumVisibleInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan MaximumVisibleInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MinimumHiddenInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan MaximumHiddenInterval = TimeSpan.FromSeconds(10);

    public static TelemetryCadenceMode SelectMode(bool anySurfaceVisible, bool isLatched, string? latchType)
    {
        _ = isLatched;
        _ = latchType;
        return anySurfaceVisible ? TelemetryCadenceMode.Visible : TelemetryCadenceMode.HiddenAuto;
    }

    public static TimeSpan? IntervalFor(TelemetryCadenceMode mode)
        => IntervalFor(mode, DefaultVisibleInterval, DefaultHiddenInterval);

    public static TimeSpan? IntervalFor(TelemetryCadenceMode mode, TimeSpan visibleInterval, TimeSpan hiddenInterval) => mode switch
    {
        TelemetryCadenceMode.Visible => NormalizeVisible(visibleInterval),
        TelemetryCadenceMode.HiddenAuto => NormalizeHidden(hiddenInterval),
        _ => null
    };

    public static TimeSpan NormalizeVisible(TimeSpan value)
        => Clamp(value, MinimumVisibleInterval, MaximumVisibleInterval);

    public static TimeSpan NormalizeHidden(TimeSpan value)
        => Clamp(value, MinimumHiddenInterval, MaximumHiddenInterval);

    private static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum)
    {
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }
}