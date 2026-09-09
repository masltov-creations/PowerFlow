namespace PowerFlow.App.Telemetry;

public enum TelemetryCadenceMode
{
    Off,
    HiddenAuto,
    Visible
}

public static class TelemetryCadencePolicy
{
    public static TelemetryCadenceMode SelectMode(bool anySurfaceVisible, bool isLatched, string? latchType)
    {
        if (anySurfaceVisible) return TelemetryCadenceMode.Visible;
        if (isLatched && (string.Equals(latchType, "Game", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(latchType, "Manual", StringComparison.OrdinalIgnoreCase)))
            return TelemetryCadenceMode.Off;
        return TelemetryCadenceMode.HiddenAuto;
    }

    public static TimeSpan? IntervalFor(TelemetryCadenceMode mode) => mode switch
    {
        TelemetryCadenceMode.Visible => TimeSpan.FromSeconds(1),
        TelemetryCadenceMode.HiddenAuto => TimeSpan.FromSeconds(5),
        _ => null
    };
}
