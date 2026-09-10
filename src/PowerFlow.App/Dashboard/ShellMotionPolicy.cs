namespace PowerFlow.App.Dashboard;

public static class ShellMotionPolicy
{
    public static TimeSpan Duration(PowerFlowShellState from, PowerFlowShellState to, bool reducedMotion)
    {
        if (reducedMotion || from == to) return TimeSpan.Zero;
        return (from, to) switch
        {
            (PowerFlowShellState.Hidden, PowerFlowShellState.Glance) => TimeSpan.FromMilliseconds(130),
            (PowerFlowShellState.Glance, PowerFlowShellState.Compact) => TimeSpan.FromMilliseconds(180),
            (PowerFlowShellState.Compact, PowerFlowShellState.Expanded) => TimeSpan.FromMilliseconds(210),
            (PowerFlowShellState.Expanded, PowerFlowShellState.Compact) => TimeSpan.FromMilliseconds(190),
            (PowerFlowShellState.Compact, PowerFlowShellState.Glance) => TimeSpan.FromMilliseconds(170),
            (PowerFlowShellState.Glance, PowerFlowShellState.Hidden) => TimeSpan.FromMilliseconds(130),
            (_, PowerFlowShellState.Hidden) => TimeSpan.FromMilliseconds(180),
            (PowerFlowShellState.Hidden, _) => TimeSpan.FromMilliseconds(190),
            (PowerFlowShellState.Expanded, PowerFlowShellState.FullScreen) => TimeSpan.FromMilliseconds(190),
            (PowerFlowShellState.FullScreen, PowerFlowShellState.Expanded) => TimeSpan.FromMilliseconds(180),
            _ => TimeSpan.FromMilliseconds(IsGrowth(from, to) ? 210 : 190)
        };
    }

    public static bool IsGrowth(PowerFlowShellState from, PowerFlowShellState to)
        => Rank(to) > Rank(from);

    public static double Ease(PowerFlowShellState from, PowerFlowShellState to, double progress)
    {
        var t = Math.Clamp(progress, 0d, 1d);
        if (IsGrowth(from, to)) return 1d - Math.Pow(1d - t, 3d);
        return t * t * (3d - (2d * t));
    }

    public static double DetailProgress(PowerFlowShellState from, PowerFlowShellState to, double progress)
    {
        var t = Math.Clamp(progress, 0d, 1d);
        if (from == to) return 1d;
        if (IsGrowth(from, to))
            return Math.Clamp((t - (1d / 3d)) / (2d / 3d), 0d, 1d);
        return Math.Clamp(1d - (t / 0.67d), 0d, 1d);
    }

    public static double ModeMorphProgress(double t) => Window(t, 0.10, 0.65);
    public static double StatsMorphProgress(double t) => Window(t, 0.15, 0.80);
    public static double ContextMorphProgress(double t) => Window(t, 0.35, 0.90);
    public static double NavigationProgress(double t, bool collapsing = false)
        => collapsing ? 1d - Window(t, 0.05, 0.35) : Window(t, 0.65, 0.95);
    public static double PrimaryAnchorProgress(double t, bool collapsing = false)
        => collapsing ? 1d - Window(t, 0.55, 0.95) : Window(t, 0.05, 0.90);

    private static double Window(double value, double start, double end)
        => Math.Clamp((Math.Clamp(value, 0d, 1d) - start) / Math.Max(.001d, end - start), 0d, 1d);

    public static int Rank(PowerFlowShellState state) => state switch
    {
        PowerFlowShellState.Hidden => 0,
        PowerFlowShellState.Glance => 1,
        PowerFlowShellState.Compact => 2,
        PowerFlowShellState.Expanded => 3,
        PowerFlowShellState.FullScreen => 4,
        _ => 0
    };
}
