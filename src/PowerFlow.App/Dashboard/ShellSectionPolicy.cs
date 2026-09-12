namespace PowerFlow.App.Dashboard;

public static class ShellSectionPolicy
{
    public static PowerFlowShellState MinimumState(string? section)
        => string.Equals(section, "flow", StringComparison.OrdinalIgnoreCase)
            ? PowerFlowShellState.Compact
            : PowerFlowShellState.Expanded;
}
