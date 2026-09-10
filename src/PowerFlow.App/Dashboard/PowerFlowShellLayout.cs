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
                ModePresentation.CurrentChip,
                StatsPresentation.Inline,
                TrajectoryPresentation.Minimal,
                ControlContextPresentation.CauseLine,
                SecondaryPresentation.Hidden,
                new ShellGeometry(0, 5, 3, 22, 22, 0.72, 20, 0)),

            PowerFlowShellState.Compact => new(
                state,
                NavigationPresentation.Overlay,
                HeaderPresentation.Compact,
                ModePresentation.Segmented,
                StatsPresentation.CompactRail,
                TrajectoryPresentation.Compact,
                ControlContextPresentation.Rail,
                SecondaryPresentation.Hidden,
                new ShellGeometry(0, 10, 8, 42, 48, 0.68, 62, 0)),

            PowerFlowShellState.FullScreen => Expanded(width, height, state, fullDensity: true),
            PowerFlowShellState.Expanded => Expanded(width, height, state, fullDensity: false),
            _ => new(
                PowerFlowShellState.Hidden,
                NavigationPresentation.None,
                HeaderPresentation.Minimal,
                ModePresentation.CurrentChip,
                StatsPresentation.Inline,
                TrajectoryPresentation.Minimal,
                ControlContextPresentation.CauseLine,
                SecondaryPresentation.Hidden,
                new ShellGeometry(0, 0, 0, 0, 0, 0.70, 0, 0))
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
        var headerHeight = 44 + 8 * fluid;
        var modeBandHeight = 62 + 12 * fluid;
        var graphFraction = Math.Clamp(0.68 + 0.04 * widthProgress, 0.68, 0.72);
        var controlBandHeight = 92 + 30 * fluid;
        var secondaryBandHeight = 104 + 34 * fluid;

        if (fullDensity)
        {
            headerHeight += 4;
            modeBandHeight += 4;
            controlBandHeight += 10;
            secondaryBandHeight += 14;
        }

        return new ShellPresentationProfile(
            state,
            NavigationPresentation.Rail,
            HeaderPresentation.System,
            ModePresentation.Cards,
            StatsPresentation.FullRail,
            TrajectoryPresentation.Full,
            ControlContextPresentation.Modules,
            SecondaryPresentation.Full,
            new ShellGeometry(
                navigationWidth,
                padding,
                gap,
                headerHeight,
                modeBandHeight,
                graphFraction,
                controlBandHeight,
                secondaryBandHeight));
    }
}
