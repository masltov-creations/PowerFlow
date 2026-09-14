using PowerFlow.App.Tray;
using Windows.Graphics;

namespace PowerFlow.App.Dashboard;

public static class ShellTransitionGeometry
{
    private const int TrayGap = 10;

    public static RectInt32 TargetBounds(TrayRect tray, TrayRect workArea, RectInt32 current, PowerFlowShellState target, double rasterizationScale = 1d)
    {
        if (target == PowerFlowShellState.FullScreen)
            return new RectInt32(workArea.Left, workArea.Top, workArea.Width, workArea.Height);

        if (target == PowerFlowShellState.Hidden)
        {
            var hiddenWidth = Math.Min(Math.Max(1, tray.Width), Math.Max(1, workArea.Width));
            var hiddenHeight = Math.Min(Math.Max(1, tray.Height), Math.Max(1, workArea.Height));
            var x = Math.Clamp(tray.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - hiddenWidth));
            var y = Math.Clamp(tray.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - hiddenHeight));
            return new RectInt32(x, y, hiddenWidth, hiddenHeight);
        }

        var logical = target switch
        {
            PowerFlowShellState.Glance => new ShellLogicalSize(ShellResizeStateProjection.MinimumWidth, ShellResizeStateProjection.MinimumHeight),
            PowerFlowShellState.Compact => new ShellLogicalSize(760, 440),
            PowerFlowShellState.Expanded => new ShellLogicalSize(1280, 800),
            PowerFlowShellState.Workspace => new ShellLogicalSize(1360, 860),
            _ => ShellCoordinateProjection.ToLogicalSize(Math.Max(1, current.Width), Math.Max(1, current.Height), rasterizationScale)
        };
        var requested = ShellCoordinateProjection.ToPhysicalSize(logical.Width, logical.Height, rasterizationScale);
        var width = Math.Min(requested.Width, Math.Max(1, workArea.Width));
        var height = Math.Min(requested.Height, Math.Max(1, workArea.Height));
        var trayAnchored = target == PowerFlowShellState.Glance
            || (target == PowerFlowShellState.Compact && current.Width <= 360 && current.Height <= 220);
        var desiredX = trayAnchored
            ? tray.Right - width
            : current.X + current.Width / 2 - width / 2;
        var desiredY = trayAnchored
            ? tray.Top - TrayGap - height
            : current.Y + current.Height / 2 - height / 2;
        var maxX = workArea.Right - width;
        var maxY = workArea.Bottom - height;
        var xClamped = Math.Clamp(desiredX, workArea.Left, Math.Max(workArea.Left, maxX));
        var yClamped = Math.Clamp(desiredY, workArea.Top, Math.Max(workArea.Top, maxY));
        return new RectInt32(xClamped, yClamped, width, height);
    }

    public static RectInt32 TraySeedBounds(TrayRect tray, TrayRect workArea, int width, int height)
    {
        var seedWidth = Math.Min(Math.Max(1, width), Math.Max(1, workArea.Width));
        var seedHeight = Math.Min(Math.Max(1, height), Math.Max(1, workArea.Height));
        var desiredX = tray.Right - seedWidth;
        var desiredY = tray.Top - TrayGap - seedHeight;
        var maxX = workArea.Right - seedWidth;
        var maxY = workArea.Bottom - seedHeight;
        return new RectInt32(
            Math.Clamp(desiredX, workArea.Left, Math.Max(workArea.Left, maxX)),
            Math.Clamp(desiredY, workArea.Top, Math.Max(workArea.Top, maxY)),
            seedWidth,
            seedHeight);
    }
    public static RectInt32 Interpolate(RectInt32 start, RectInt32 end, double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        if (p <= 0) return start;
        if (p >= 1) return end;
        return new RectInt32(
            Lerp(start.X, end.X, p),
            Lerp(start.Y, end.Y, p),
            Lerp(start.Width, end.Width, p),
            Lerp(start.Height, end.Height, p));
    }

    public static ShellGeometry InterpolateGeometry(ShellGeometry start, ShellGeometry end, double progress)
    {
        var p = Math.Clamp(progress, 0d, 1d);
        return new ShellGeometry(
            Lerp(start.NavigationWidth, end.NavigationWidth, p),
            Lerp(start.ContentPadding, end.ContentPadding, p),
            Lerp(start.Gap, end.Gap, p),
            Lerp(start.HeaderHeight, end.HeaderHeight, p),
            Lerp(start.ControlBandHeight, end.ControlBandHeight, p));
    }

    private static double Lerp(double start, double end, double progress)
        => start + (end - start) * progress;
    private static int Lerp(int start, int end, double progress)
        => (int)Math.Round(start + (end - start) * progress, MidpointRounding.AwayFromZero);
}
