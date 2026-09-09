namespace PowerFlow.App.Dashboard;

public enum ShellInteraction
{
    TraySingleClick,
    TrayDoubleClick,
    SurfaceClick,
    Expand,
    Collapse,
    Hide,
    OpenRules,
    OpenSettings
}

public static class ShellStateTransition
{
    public static PowerFlowShellState Next(PowerFlowShellState current, ShellInteraction interaction) => interaction switch
    {
        ShellInteraction.TraySingleClick => PowerFlowShellState.Glance,
        ShellInteraction.TrayDoubleClick => PowerFlowShellState.Compact,
        ShellInteraction.SurfaceClick when current == PowerFlowShellState.Glance => PowerFlowShellState.Compact,
        ShellInteraction.Expand when current is PowerFlowShellState.Compact or PowerFlowShellState.Glance => PowerFlowShellState.Expanded,
        ShellInteraction.Expand when current == PowerFlowShellState.Expanded => PowerFlowShellState.FullScreen,
        ShellInteraction.Collapse when current == PowerFlowShellState.FullScreen => PowerFlowShellState.Expanded,
        ShellInteraction.Collapse when current == PowerFlowShellState.Expanded => PowerFlowShellState.Compact,
        ShellInteraction.Hide => PowerFlowShellState.Hidden,
        ShellInteraction.OpenRules or ShellInteraction.OpenSettings => PowerFlowShellState.Expanded,
        _ => current
    };
}