namespace PowerFlow.App.Dashboard;

public sealed record ShellManualResizePresentation(
    ShellDensity Density,
    PowerFlowShellState State,
    double Disclosure);

public static class ShellManualResizePolicy
{
    public static ShellManualResizePresentation Resolve(
        ShellLogicalSize size,
        ShellDensity previousDensity,
        PowerFlowShellState currentState)
    {
        var density = ShellResponsiveDensity.Resolve(size, previousDensity);
        var state = currentState switch
        {
            PowerFlowShellState.Hidden or PowerFlowShellState.Glance or PowerFlowShellState.FullScreen => currentState,
            _ when size.Width >= 1320 && size.Height >= 820 => PowerFlowShellState.Workspace,
            _ when density == ShellDensity.Expanded => PowerFlowShellState.Expanded,
            _ => PowerFlowShellState.Compact
        };
        return new ShellManualResizePresentation(density, state, ShellDisclosurePolicy.Progress(size, state));
    }
}