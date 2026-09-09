using PowerFlow.App.Dashboard;
using Xunit;

namespace PowerFlow.App.Tests.Dashboard;

public sealed class ShellStateTransitionTests
{
    [Theory]
    [InlineData(PowerFlowShellState.Hidden, ShellInteraction.TraySingleClick, PowerFlowShellState.Glance)]
    [InlineData(PowerFlowShellState.Glance, ShellInteraction.SurfaceClick, PowerFlowShellState.Compact)]
    [InlineData(PowerFlowShellState.Compact, ShellInteraction.Expand, PowerFlowShellState.Expanded)]
    [InlineData(PowerFlowShellState.Expanded, ShellInteraction.Collapse, PowerFlowShellState.Compact)]
    [InlineData(PowerFlowShellState.FullScreen, ShellInteraction.Collapse, PowerFlowShellState.Expanded)]
    [InlineData(PowerFlowShellState.Glance, ShellInteraction.Hide, PowerFlowShellState.Hidden)]
    public void Next_UsesGrowShrinkContract(PowerFlowShellState current, ShellInteraction interaction, PowerFlowShellState expected)
        => Assert.Equal(expected, ShellStateTransition.Next(current, interaction));

    [Theory]
    [InlineData(PowerFlowShellState.Hidden)]
    [InlineData(PowerFlowShellState.Glance)]
    [InlineData(PowerFlowShellState.Compact)]
    public void RulesAndSettings_RequireExpandedShell(PowerFlowShellState current)
    {
        Assert.Equal(PowerFlowShellState.Expanded, ShellStateTransition.Next(current, ShellInteraction.OpenRules));
        Assert.Equal(PowerFlowShellState.Expanded, ShellStateTransition.Next(current, ShellInteraction.OpenSettings));
    }

    [Fact]
    public void TrayDoubleClick_GoesDirectlyToCompact()
        => Assert.Equal(PowerFlowShellState.Compact, ShellStateTransition.Next(PowerFlowShellState.Hidden, ShellInteraction.TrayDoubleClick));
}