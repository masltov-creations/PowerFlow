namespace PowerFlow.App.Dashboard;

public sealed record PowerFlowShellLayoutProfile(
    PowerFlowShellState State,
    bool ShowNavigationRail,
    bool ShowModeCards,
    bool ShowGlanceTelemetry,
    bool ShowCompactTelemetry,
    bool ShowLiveStatsPanel,
    bool ShowLowerContextPanels,
    double GraphHeight,
    double NavigationWidth,
    double PanelGap,
    double ContentPadding);

public static class PowerFlowShellLayout
{
    public static PowerFlowShellLayoutProfile Resolve(int width, int height, PowerFlowShellState requestedState, string section)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        section = string.IsNullOrWhiteSpace(section) ? "flow" : section;

        var state = requestedState;
        if (!string.Equals(section, "flow", StringComparison.OrdinalIgnoreCase) && state == PowerFlowShellState.Compact)
            state = PowerFlowShellState.Expanded;

        return state switch
        {
            PowerFlowShellState.Hidden => new(state, false, false, false, false, false, false, 0, 0, 0, 0),
            PowerFlowShellState.Glance => new(state, false, false, true, false, false, false,
                Math.Clamp(height - 110d, 48, 66), 0, 6, 10),
            PowerFlowShellState.Compact => new(state, false, true, false, true, false, false,
                Math.Clamp(height - 235d, 175, 235), 0, 10, 12),
            PowerFlowShellState.FullScreen => Expanded(width, height, PowerFlowShellState.FullScreen, fullDensity: true),
            _ => Expanded(width, height, PowerFlowShellState.Expanded, fullDensity: false)
        };
    }

    private static PowerFlowShellLayoutProfile Expanded(int width, int height, PowerFlowShellState state, bool fullDensity)
    {
        var w = Math.Clamp((width - 900d) / 500d, 0, 1);
        var h = Math.Clamp((height - 580d) / 360d, 0, 1);
        var fluid = Math.Min(w, h);
        var graph = Math.Clamp(height * (0.48 + 0.04 * fluid), 300, fullDensity ? 620 : 520);
        var nav = 120 + 28 * w;
        var gap = 12 + 8 * fluid;
        var padding = 14 + 8 * fluid;
        return new PowerFlowShellLayoutProfile(
            state,
            true,
            true,
            false,
            false,
            true,
            true,
            graph,
            nav,
            gap,
            padding);
    }
}