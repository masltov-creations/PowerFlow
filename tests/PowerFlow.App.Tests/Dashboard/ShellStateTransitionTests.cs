using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellStateTransitionTests
{
    [Theory]
    [InlineData(PowerFlowShellState.Hidden, ShellInteraction.TraySingleClick, PowerFlowShellState.Glance)]
    [InlineData(PowerFlowShellState.Glance, ShellInteraction.SurfaceClick, PowerFlowShellState.Compact)]
    [InlineData(PowerFlowShellState.Compact, ShellInteraction.Expand, PowerFlowShellState.Expanded)]
    [InlineData(PowerFlowShellState.Expanded, ShellInteraction.Expand, PowerFlowShellState.Workspace)]
    [InlineData(PowerFlowShellState.Workspace, ShellInteraction.Collapse, PowerFlowShellState.Expanded)]
    [InlineData(PowerFlowShellState.Expanded, ShellInteraction.Collapse, PowerFlowShellState.Compact)]
    [InlineData(PowerFlowShellState.FullScreen, ShellInteraction.Collapse, PowerFlowShellState.Workspace)]
    [InlineData(PowerFlowShellState.Glance, ShellInteraction.Hide, PowerFlowShellState.Hidden)]
    public void Next_UsesGrowShrinkContract(PowerFlowShellState current, ShellInteraction interaction, PowerFlowShellState expected)
        => Assert.Equal(expected, ShellStateTransition.Next(current, interaction));

    [Fact]
    public void FullScreen_IsAnExplicitCommandFromWorkspace()
        => Assert.Equal(PowerFlowShellState.FullScreen, ShellStateTransition.Next(PowerFlowShellState.Workspace, ShellInteraction.FullScreen));

    [Theory]
    [InlineData(PowerFlowShellState.Glance)]
    [InlineData(PowerFlowShellState.Compact)]
    [InlineData(PowerFlowShellState.Expanded)]
    [InlineData(PowerFlowShellState.Workspace)]
    public void NavigationInteractions_PreserveCurrentShellState(PowerFlowShellState current)
    {
        Assert.Equal(current, ShellStateTransition.Next(current, ShellInteraction.OpenRules));
        Assert.Equal(current, ShellStateTransition.Next(current, ShellInteraction.OpenSettings));
    }

    [Fact]
    public void TrayDoubleClick_GoesDirectlyToCompact()
        => Assert.Equal(PowerFlowShellState.Compact, ShellStateTransition.Next(PowerFlowShellState.Hidden, ShellInteraction.TrayDoubleClick));
}