namespace PowerFlow.App.Dashboard;

public static class PowerFlowShellLayout
{
    public static ShellPresentationProfile Resolve(int width, int height, PowerFlowShellState requestedState, string section)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        section = string.IsNullOrWhiteSpace(section) ? "flow" : section;

        var state = requestedState;
        if (!string.Equals(section, "flow", StringComparison.OrdinalIgnoreCase) && state == PowerFlowShellState.Compact)
            state = PowerFlowShellState.Expanded;

        return state switch
        {
            PowerFlowShellState.Glance => new(
                state,
                NavigationPresentation.None,
                HeaderPresentation.Minimal,
                TimelinePresentation.Glance,
                GovernorControlPresentation.Summary,
                new ShellGeometry(0, 5, 3, 22, 0)),

            PowerFlowShellState.Compact => new(
                state,
                NavigationPresentation.Overlay,
                HeaderPresentation.Compact,
                TimelinePresentation.Compact,
                GovernorControlPresentation.Bias,
                new ShellGeometry(0, 10, 8, 42, 82)),

            PowerFlowShellState.FullScreen => Expanded(width, height, state, fullDensity: true),
            PowerFlowShellState.Expanded => Expanded(width, height, state, fullDensity: false),
            _ => new(
                PowerFlowShellState.Hidden,
                NavigationPresentation.None,
                HeaderPresentation.Minimal,
                TimelinePresentation.Glance,
                GovernorControlPresentation.Summary,
                new ShellGeometry(0, 0, 0, 0, 0))
        };
    }

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