namespace PowerFlow.App.Dashboard;

public static class ShellResizeStateProjection
{
    public const int MinimumWidth = 320;
    public const int MinimumHeight = 176;
    public const int EnterCompactWidth = 520;
    public const int EnterCompactHeight = 300;
    public const int ExitCompactWidth = 480;
    public const int ExitCompactHeight = 270;

    public static ShellLogicalSize ClampMinimum(ShellLogicalSize size) => new(
        Math.Max(MinimumWidth, size.Width),
        Math.Max(MinimumHeight, size.Height));

    public static PowerFlowShellState Resolve(ShellLogicalSize size, PowerFlowShellState previous)
    {
        if (previous == PowerFlowShellState.FullScreen) return PowerFlowShellState.FullScreen;

        if (previous == PowerFlowShellState.Glance)
            return size.Width >= EnterCompactWidth && size.Height >= EnterCompactHeight
                ? PowerFlowShellState.Compact
                : PowerFlowShellState.Glance;

        if (size.Width <= ExitCompactWidth || size.Height <= ExitCompactHeight)
            return PowerFlowShellState.Glance;

        var previousDensity = previous is PowerFlowShellState.Expanded or PowerFlowShellState.Workspace
            ? ShellDensity.Expanded
            : ShellDensity.Compact;
        return ShellResponsiveDensity.Resolve(size, previousDensity) == ShellDensity.Expanded
            ? PowerFlowShellState.Expanded
            : PowerFlowShellState.Compact;
    }
}