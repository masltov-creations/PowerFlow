using PowerFlow.App.Controller;

namespace PowerFlow.App.Telemetry;

public static class GovernorPressureProjection
{
    public static double FromSample(ContinuitySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return Valid(sample.DemandPressure?.PressurePercent) ?? Math.Clamp(sample.CpuPercent, 0d, 100d);
    }

    public static double Current(ControllerSnapshot snapshot, ContinuitySample? latestRich)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return latestRich is null
            ? Math.Clamp(snapshot.CpuPercent, 0d, 100d)
            : Valid(latestRich.DemandPressure?.PressurePercent) ?? Math.Clamp(snapshot.CpuPercent, 0d, 100d);
    }

    private static double? Valid(double? pressure)
        => pressure is double value && double.IsFinite(value) && value is >= 0d and <= 100d ? value : null;
}