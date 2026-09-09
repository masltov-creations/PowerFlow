using PowerFlow.App.Tray;
using Windows.Graphics;

namespace PowerFlow.App.Dashboard;

public static class ShellTransitionGeometry
{
    private const int TrayGap = 10;

    public static RectInt32 TargetBounds(TrayRect tray, TrayRect workArea, RectInt32 current, PowerFlowShellState target)
    {
        if (target == PowerFlowShellState.FullScreen)
            return new RectInt32(workArea.Left, workArea.Top, workArea.Width, workArea.Height);

        var (requestedWidth, requestedHeight) = target switch
        {
            PowerFlowShellState.Hidden => (1, 1),
            PowerFlowShellState.Glance => (320, 176),
            PowerFlowShellState.Compact => (760, 440),
            PowerFlowShellState.Expanded => (1280, 800),
            _ => (Math.Max(1, current.Width), Math.Max(1, current.Height))
        };

        var width = Math.Min(requestedWidth, Math.Max(1, workArea.Width));
        var height = Math.Min(requestedHeight, Math.Max(1, workArea.Height));

        if (target == PowerFlowShellState.Hidden)
        {
            var x = Math.Clamp(tray.Left + tray.Width / 2, workArea.Left, workArea.Right - width);
            var y = Math.Clamp(tray.Top + tray.Height / 2, workArea.Top, workArea.Bottom - height);
            return new RectInt32(x, y, width, height);
        }

        var desiredX = tray.Right - width;
        var desiredY = tray.Top - TrayGap - height;
        var maxX = workArea.Right - width;
        var maxY = workArea.Bottom - height;
        var xClamped = Math.Clamp(desiredX, workArea.Left, Math.Max(workArea.Left, maxX));
        var yClamped = Math.Clamp(desiredY, workArea.Top, Math.Max(workArea.Top, maxY));
        return new RectInt32(xClamped, yClamped, width, height);
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

    private static int Lerp(int start, int end, double progress)
        => (int)Math.Round(start + (end - start) * progress, MidpointRounding.AwayFromZero);
}