namespace PowerFlow.App.Dashboard;

public static class ShellDisclosurePolicy
{
    public static double Progress(ShellLogicalSize size, PowerFlowShellState state)
    {
        if (state is PowerFlowShellState.Hidden or PowerFlowShellState.Glance) return 0d;
        if (state is PowerFlowShellState.Workspace or PowerFlowShellState.FullScreen) return 1d;

        var width = Piecewise(size.Width, 760d, 900d, 1280d, .18d, .42d, .82d);
        var height = Piecewise(size.Height, 440d, 560d, 800d, .18d, .42d, .82d);
        var supported = Math.Min(width, height);
        return Math.Clamp(supported, .18d, .94d);
    }

    public static double ContextProgress(double disclosure)
        => SmoothWindow(disclosure, .28d, .68d);

    public static double FooterProgress(double disclosure)
        => SmoothWindow(disclosure, .58d, .90d);

    public static double DeepProgress(double disclosure)
        => SmoothWindow(disclosure, .72d, 1d);

    public static double SettledContextProgress(double disclosure)
    {
        var context = ContextProgress(disclosure);
        return context < .86d ? 0d : context;
    }

    public static double SettledFooterProgress(double disclosure)
    {
        var footer = FooterProgress(disclosure);
        return footer < .80d ? 0d : footer;
    }

    private static double Piecewise(double value, double compact, double threshold, double dashboard, double compactValue, double thresholdValue, double dashboardValue)
    {
        if (value <= compact) return compactValue;
        if (value < threshold) return Lerp(compactValue, thresholdValue, Smooth01((value - compact) / (threshold - compact)));
        if (value < dashboard) return Lerp(thresholdValue, dashboardValue, Smooth01((value - threshold) / (dashboard - threshold)));
        return Lerp(dashboardValue, 1d, Smooth01((value - dashboard) / Math.Max(1d, dashboard * .08d)));
    }

    private static double SmoothWindow(double value, double start, double end)
        => Smooth01((Math.Clamp(value, 0d, 1d) - start) / Math.Max(.001d, end - start));

    private static double Smooth01(double value)
    {
        var t = Math.Clamp(value, 0d, 1d);
        return t * t * (3d - 2d * t);
    }

    private static double Lerp(double from, double to, double progress)
        => from + (to - from) * progress;
}
