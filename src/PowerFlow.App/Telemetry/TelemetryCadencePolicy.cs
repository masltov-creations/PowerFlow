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
        _ = isLatched;
        _ = latchType;
        return anySurfaceVisible ? TelemetryCadenceMode.Visible : TelemetryCadenceMode.HiddenAuto;
    }

    public static TimeSpan? IntervalFor(TelemetryCadenceMode mode) => mode switch
    {
        TelemetryCadenceMode.Visible => TimeSpan.FromSeconds(1),
        TelemetryCadenceMode.HiddenAuto => TimeSpan.FromSeconds(5),
        _ => null
    };
}
