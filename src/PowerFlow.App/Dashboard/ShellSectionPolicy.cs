namespace PowerFlow.App.Dashboard;

public static class ShellSectionPolicy
{
    public static PowerFlowShellState MinimumState(string? section) => PowerFlowShellState.Compact;
}