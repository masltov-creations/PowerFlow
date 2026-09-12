namespace PowerFlow.App.Dashboard;

public static class ShellPresenterTransitionPolicy
{
    public static bool RequiresLayoutBarrier(PowerFlowShellState from, PowerFlowShellState to)
        => from == PowerFlowShellState.FullScreen && to != PowerFlowShellState.FullScreen;
}
