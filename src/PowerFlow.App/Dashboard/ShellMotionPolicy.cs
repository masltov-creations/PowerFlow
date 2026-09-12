namespace PowerFlow.App.Dashboard;

public static class ShellMotionPolicy
{
    public static TimeSpan Duration(PowerFlowShellState from, PowerFlowShellState to, bool reducedMotion)
    {
        if (reducedMotion || from == to) return TimeSpan.Zero;
        return (from, to) switch
        {
            (PowerFlowShellState.Hidden, PowerFlowShellState.Glance) => TimeSpan.FromMilliseconds(130),
            (PowerFlowShellState.Glance, PowerFlowShellState.Compact) => TimeSpan.FromMilliseconds(260),
            (PowerFlowShellState.Compact, PowerFlowShellState.Expanded) => TimeSpan.FromMilliseconds(340),
            (PowerFlowShellState.Expanded, PowerFlowShellState.Compact) => TimeSpan.FromMilliseconds(320),
            (PowerFlowShellState.Compact, PowerFlowShellState.Glance) => TimeSpan.FromMilliseconds(240),
            (PowerFlowShellState.Glance, PowerFlowShellState.Hidden) => TimeSpan.FromMilliseconds(130),
            (_, PowerFlowShellState.Hidden) => TimeSpan.FromMilliseconds(220),
            (PowerFlowShellState.Hidden, _) => TimeSpan.FromMilliseconds(260),
            (PowerFlowShellState.Expanded, PowerFlowShellState.Workspace) => TimeSpan.FromMilliseconds(380),
            (PowerFlowShellState.Workspace, PowerFlowShellState.Expanded) => TimeSpan.FromMilliseconds(340),
            (PowerFlowShellState.Workspace, PowerFlowShellState.FullScreen) => TimeSpan.FromMilliseconds(360),
            (PowerFlowShellState.FullScreen, PowerFlowShellState.Workspace) => TimeSpan.FromMilliseconds(340),
            (PowerFlowShellState.FullScreen, PowerFlowShellState.Compact) => TimeSpan.FromMilliseconds(380),
            _ => TimeSpan.FromMilliseconds(IsGrowth(from, to) ? 340 : 320)
        };
    }

    public static TimeSpan DurationForTravel(PowerFlowShellState from, PowerFlowShellState to, bool reducedMotion, double pixelTravel)
    {
        var baseline = Duration(from, to, reducedMotion);
        if (reducedMotion || baseline == TimeSpan.Zero) return baseline;
        var travel = Math.Max(0d, pixelTravel);
        var longTravelExtra = Math.Max(0d, travel - 700d) * 0.90d;
        var travelMs = Math.Clamp(180d + travel * 0.40d + longTravelExtra, baseline.TotalMilliseconds, 1250d);
        return TimeSpan.FromMilliseconds(travelMs);
    }
    public static bool IsGrowth(PowerFlowShellState from, PowerFlowShellState to) => Rank(to) > Rank(from);

    public static double Ease(PowerFlowShellState from, PowerFlowShellState to, double progress)
    {
        var t = Math.Clamp(progress, 0d, 1d);
        return t * t * t * (t * (t * 6d - 15d) + 10d);
    }

    public static double DetailProgress(PowerFlowShellState from, PowerFlowShellState to, double progress)
    {
        var t = Math.Clamp(progress, 0d, 1d);
        if (from == to) return 1d;
        if (IsGrowth(from, to)) return Window(t, .18, .88);
        return 1d - Window(t, .12, .78);
    }

    public static double ModeMorphProgress(double t) => Window(t, .08, .82);
    public static double StatsMorphProgress(double t) => Window(t, .12, .88);
    public static double ContextMorphProgress(double t) => Window(t, .20, .94);
    public static double NavigationProgress(double t, bool collapsing = false)
        => collapsing ? 1d - Window(t, .12, .78) : Window(t, .18, .88);
    public static double PrimaryAnchorProgress(double t, bool collapsing = false)
        => collapsing ? 1d - Window(t, .10, .90) : Window(t, .10, .90);

    private static double Window(double value, double start, double end)
    {
        var t = Math.Clamp((Math.Clamp(value, 0d, 1d) - start) / Math.Max(.001d, end - start), 0d, 1d);
        return t * t * (3d - 2d * t);
    }

    public static int Rank(PowerFlowShellState state) => state switch
    {
        PowerFlowShellState.Hidden => 0,
        PowerFlowShellState.Glance => 1,
        PowerFlowShellState.Compact => 2,
        PowerFlowShellState.Expanded => 3,
        PowerFlowShellState.Workspace => 4,
        PowerFlowShellState.FullScreen => 5,
        _ => 0
    };
}
