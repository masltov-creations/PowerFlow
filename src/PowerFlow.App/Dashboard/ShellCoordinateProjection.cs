namespace PowerFlow.App.Dashboard;

public readonly record struct ShellLogicalSize(int Width, int Height);
public readonly record struct ShellPhysicalSize(int Width, int Height);

public static class ShellCoordinateProjection
{
    public static ShellLogicalSize ToLogicalSize(int physicalWidth, int physicalHeight, double rasterizationScale)
    {
        var scale = NormalizeScale(rasterizationScale);
        return new ShellLogicalSize(
            Math.Max(1, (int)Math.Round(physicalWidth / scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(physicalHeight / scale, MidpointRounding.AwayFromZero)));
    }

    public static ShellPhysicalSize ToPhysicalSize(int logicalWidth, int logicalHeight, double rasterizationScale)
    {
        var scale = NormalizeScale(rasterizationScale);
        return new ShellPhysicalSize(
            Math.Max(1, (int)Math.Round(Math.Max(1, logicalWidth) * scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(Math.Max(1, logicalHeight) * scale, MidpointRounding.AwayFromZero)));
    }

    private static double NormalizeScale(double value)
        => double.IsFinite(value) && value > 0 ? value : 1d;
}