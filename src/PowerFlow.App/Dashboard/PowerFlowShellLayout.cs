namespace PowerFlow.App.Dashboard;

public static class PowerFlowShellLayout
{
    public static ShellPresentationProfile Resolve(int width, int height, PowerFlowShellState requestedState, string section)
        => Resolve(width, height, requestedState, section, DefaultDensity(requestedState));

    public static ShellPresentationProfile Resolve(int width, int height, PowerFlowShellState requestedState, string section, ShellDensity density)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        section = string.IsNullOrWhiteSpace(section) ? "flow" : section;

        if (requestedState == PowerFlowShellState.Hidden)
            return new ShellPresentationProfile(
                requestedState,
                NavigationPresentation.None,
                HeaderPresentation.Minimal,
                TimelinePresentation.Glance,
                GovernorControlPresentation.Summary,
                new ShellGeometry(0, 0, 0, 0, 0));

        if (requestedState == PowerFlowShellState.Glance)
            return new ShellPresentationProfile(
                requestedState,
                NavigationPresentation.None,
                HeaderPresentation.Minimal,
                TimelinePresentation.Glance,
                GovernorControlPresentation.Summary,
                new ShellGeometry(0, 5, 3, 22, 0));

        if (requestedState is PowerFlowShellState.Workspace or PowerFlowShellState.FullScreen)
            return Expanded(width, height, requestedState, fullDensity: true);

        var effectiveDensity = string.Equals(section, "flow", StringComparison.OrdinalIgnoreCase)
            ? density
            : ShellDensity.Expanded;

        return effectiveDensity == ShellDensity.Compact
            ? Compact(requestedState)
            : Expanded(width, height, requestedState, fullDensity: false);
    }

    private static ShellDensity DefaultDensity(PowerFlowShellState state)
        => state is PowerFlowShellState.Expanded or PowerFlowShellState.Workspace or PowerFlowShellState.FullScreen
            ? ShellDensity.Expanded
            : ShellDensity.Compact;

    private static ShellPresentationProfile Compact(PowerFlowShellState state) => new(
        state,
        NavigationPresentation.Overlay,
        HeaderPresentation.Compact,
        TimelinePresentation.Compact,
        GovernorControlPresentation.Bias,
        new ShellGeometry(0, 10, 8, 74, 82));

    private static ShellPresentationProfile Expanded(int width, int height, PowerFlowShellState state, bool fullDensity)
    {
        var widthProgress = Math.Clamp((width - 900d) / 500d, 0, 1);
        var heightProgress = Math.Clamp((height - 580d) / 360d, 0, 1);
        var fluid = Math.Min(widthProgress, heightProgress);

        var navigationWidth = 128 + 24 * widthProgress;
        var padding = 14 + 8 * fluid;
        var gap = 10 + 8 * fluid;
        var headerHeight = 44 + 8 * fluid + (fullDensity ? 4 : 0);
        var controlBandHeight = 108 + 32 * fluid + (fullDensity ? 12 : 0);

        return new ShellPresentationProfile(
            state,
            NavigationPresentation.Rail,
            HeaderPresentation.System,
            fullDensity ? TimelinePresentation.Full : TimelinePresentation.Expanded,
            fullDensity ? GovernorControlPresentation.Deep : GovernorControlPresentation.Contextual,
            new ShellGeometry(navigationWidth, padding, gap, headerHeight, controlBandHeight));
    }
}
