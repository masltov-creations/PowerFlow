namespace PowerFlow.App.Dashboard;

public readonly record struct ShellLogicalSize(int Width, int Height);

public static class ShellCoordinateProjection
{
    public static ShellLogicalSize ToLogicalSize(int physicalWidth, int physicalHeight, double rasterizationScale)
    {
        var scale = double.IsFinite(rasterizationScale) && rasterizationScale > 0 ? rasterizationScale : 1d;
        return new ShellLogicalSize(
            Math.Max(1, (int)Math.Round(physicalWidth / scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(physicalHeight / scale, MidpointRounding.AwayFromZero)));
    }
}
